using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Llano.App;

[Flags]
internal enum TelemetryRequest : byte { None = 0, Cpu = 1, Fps = 2 }

internal sealed record TelemetryReply(SensorReading[] Cpu, double? Fps, string? FpsStatus);
internal sealed record TelemetryReading(TelemetryReply Reply, TimeSpan ReceivedAt);

internal static class TelemetryPipe
{
    private const int MaximumReply = 96 * 1024;
    internal const int MaximumCpuReadings = 384;
    private static readonly JsonSerializerOptions ReplyJson = new()
    {
        IgnoreReadOnlyProperties = true,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true
    };

    private static string Name
    {
        get
        {
            using var process = Process.GetCurrentProcess();
            return $"LlanoUltraControl-Telemetry-v1-{StartupRegistration.Sid}-{process.SessionId}";
        }
    }

    public static NamedPipeServerStream CreateServer()
    {
        var user = new SecurityIdentifier(StartupRegistration.Sid);
        var security = new PipeSecurity();
        security.SetOwner(user);
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(Name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance, 0, 0, security);
    }

    public static NamedPipeClientStream CreateClient() =>
        new(".", Name, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Anonymous);

    public static bool IsInstalledPeer(PipeStream pipe, bool server)
    {
        uint process;
        if (!(server ? GetNamedPipeClientProcessId(pipe.SafePipeHandle, out process)
                     : GetNamedPipeServerProcessId(pipe.SafePipeHandle, out process))) return false;
        using var handle = OpenProcess(0x1000, false, process);
        string installedPath = StartupRegistration.InstalledAppPath;
        var name = new StringBuilder(installedPath.Length + 1);
        int length = name.Capacity;
        return !handle.IsInvalid && QueryFullProcessImageName(handle, 0, name, ref length) &&
            string.Equals(name.ToString(), installedPath, StringComparison.OrdinalIgnoreCase);
    }

    public static async Task WriteReplyAsync(Stream pipe, TelemetryReply reply, CancellationToken stop)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(reply, ReplyJson);
        if (body.Length > MaximumReply) throw new InvalidDataException("Telemetry reply exceeds the size limit.");
        await pipe.WriteAsync(BitConverter.GetBytes(body.Length), stop);
        await pipe.WriteAsync(body, stop);
    }

    public static async Task<TelemetryReply> ReadReplyAsync(Stream pipe, CancellationToken stop)
    {
        var length = new byte[sizeof(int)];
        await pipe.ReadExactlyAsync(length, stop);
        int count = BitConverter.ToInt32(length);
        if (count is <= 0 or > MaximumReply) throw new InvalidDataException("Invalid telemetry reply length.");
        var body = new byte[count];
        await pipe.ReadExactlyAsync(body, stop);
        var reply = JsonSerializer.Deserialize<TelemetryReply>(body, ReplyJson);
        if (reply?.Cpu is not { Length: <= MaximumCpuReadings } || reply.Fps is double fps && (!double.IsFinite(fps) || fps <= 0))
            throw new InvalidDataException("Invalid telemetry reply.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sensor in reply.Cpu)
        {
            if (sensor is not { Component: Component.Cpu, Value: double value } || !double.IsFinite(value) ||
                string.IsNullOrWhiteSpace(sensor.Id) || !ids.Add(sensor.Id) || sensor.SampledAt < TimeSpan.Zero)
                throw new InvalidDataException("Invalid CPU telemetry reading.");
        }
        return reply;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint process);
    [DllImport("kernel32.dll")]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint process);
    [DllImport("kernel32.dll")]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint process);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, int flags, StringBuilder name, ref int size);
}

internal static class TelemetryHelper
{
    public static async Task RunAsync()
    {
        using var pipe = TelemetryPipe.CreateClient();
        await pipe.ConnectAsync(10000);
        if (!TelemetryPipe.IsInstalledPeer(pipe, server: false)) return;
        var clock = Stopwatch.StartNew();
        var fps = new FpsCapture(helperSession: true);
        SensorMonitor? monitor = null;
        CancellationTokenSource? sensorStop = null;
        Task? sensorWorker = null;
        var command = new byte[1];
        try
        {
            while (true)
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                if (await pipe.ReadAsync(command, deadline.Token) == 0) break;
                var request = (TelemetryRequest)command[0];
                if (request == TelemetryRequest.None || (request & ~(TelemetryRequest.Cpu | TelemetryRequest.Fps)) != 0) break;
                if (request.HasFlag(TelemetryRequest.Cpu) && monitor is null)
                {
                    var reader = monitor = new SensorMonitor(clock);
                    sensorStop = new();
                    var stop = sensorStop.Token;
                    sensorWorker = Task.Run(() => reader.RunAsync(stop, cpuOnly: true));
                }
                else if (!request.HasFlag(TelemetryRequest.Cpu) && monitor is not null)
                {
                    await StopSensorsAsync();
                }
                bool capture = request.HasFlag(TelemetryRequest.Fps);
                fps.SetEnabled(capture);
                TimeSpan now = clock.Elapsed;
                var cpu = monitor?.Snapshot.Readings.Where(r => r.Component == Component.Cpu && r.Value.HasValue &&
                    r.SampledAt <= now && now - r.SampledAt <= TimeSpan.FromSeconds(5))
                    .Take(TelemetryPipe.MaximumCpuReadings).Select(r => r with { SampledAt = now - r.SampledAt }).ToArray() ?? [];
                await TelemetryPipe.WriteReplyAsync(pipe, new(cpu, capture ? fps.Read() : null, capture ? fps.Status : null), deadline.Token);
            }
        }
        finally
        {
            fps.StopForExit();
            await StopSensorsAsync();
        }

        async Task StopSensorsAsync()
        {
            using var stopping = sensorStop;
            var worker = sensorWorker;
            sensorStop = null;
            sensorWorker = null;
            monitor = null;
            stopping?.Cancel();
            if (worker is not null) await worker.WaitAsync(TimeSpan.FromSeconds(3));
        }
    }
}

internal sealed class TelemetryClient(Stopwatch clock)
{
    private int requested;
    private Task worker = Task.CompletedTask;
    private CancellationTokenSource? active;
    private TelemetryReading? latest;
    private string? status;

    public string? Status => Volatile.Read(ref status);
    public TelemetryReading? Snapshot =>
        Volatile.Read(ref latest) is { } reading && clock.Elapsed - reading.ReceivedAt < TimeSpan.FromSeconds(4) ? reading : null;

    public void Request(TelemetryRequest request)
    {
        if (Interlocked.Exchange(ref requested, (int)request) == (int)request) return;
        if (request == TelemetryRequest.None)
        {
            if (active is { } source)
            {
                source.Cancel();
                _ = worker.ContinueWith(_ => source.Dispose(), TaskScheduler.Default);
                active = null;
            }
            Volatile.Write(ref latest, null);
            Volatile.Write(ref status, null);
        }
        else if (active is null)
        {
            var source = active = new CancellationTokenSource();
            Task previous = worker;
            worker = Task.Run(async () =>
            {
                await previous;
                if (!source.IsCancellationRequested) await ReadAsync(source.Token);
            });
        }
    }

    private async Task ReadAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = TelemetryPipe.CreateServer();
                StartupRegistration.StartTelemetry();
                using var connect = CancellationTokenSource.CreateLinkedTokenSource(stop);
                connect.CancelAfter(TimeSpan.FromSeconds(11));
                await pipe.WaitForConnectionAsync(connect.Token);
                if (!TelemetryPipe.IsInstalledPeer(pipe, server: true)) throw new UnauthorizedAccessException("Unexpected telemetry peer.");
                Volatile.Write(ref status, "Telemetry helper connected.");
                var command = new byte[1];
                while (!stop.IsCancellationRequested)
                {
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop);
                    deadline.CancelAfter(TimeSpan.FromSeconds(5));
                    command[0] = (byte)Volatile.Read(ref requested);
                    if (command[0] == 0) break;
                    await pipe.WriteAsync(command, deadline.Token);
                    var reply = await TelemetryPipe.ReadReplyAsync(pipe, deadline.Token);
                    if (!stop.IsCancellationRequested) Volatile.Write(ref latest, new(reply, clock.Elapsed));
                    await Task.Delay(1000, stop);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or OperationCanceledException or
                InvalidDataException or JsonException or InvalidOperationException or COMException or Win32Exception)
            {
                if (stop.IsCancellationRequested) break;
                Volatile.Write(ref latest, null);
                Volatile.Write(ref status, "Telemetry helper could not connect. Check its setup in Settings.");
                Debug.WriteLine(error);
                try { await Task.Delay(TimeSpan.FromSeconds(15), stop); }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            }
        }
    }
}
