using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Security.Cryptography;

namespace Llano.App;

internal sealed class FpsCapture(bool helperSession = false)
{
    private static readonly string Executable = Path.Combine(AppContext.BaseDirectory, "PresentMon.exe");
    private readonly object gate = new();
    private readonly Dictionary<ulong, (int Count, double Milliseconds)> chains = new();
    private uint foreground;
    private CancellationTokenSource? request;
    private Task worker = Task.CompletedTask;
    private string? status;
    public string? Status => Volatile.Read(ref status);

    public void SetEnabled(bool enabled)
    {
        if (enabled == (request is not null)) return;
        if (!enabled)
        {
            var stopping = request!;
            stopping.Cancel();
            _ = worker.ContinueWith(_ => stopping.Dispose(), TaskScheduler.Default);
            request = null;
            lock (gate) chains.Clear();
            Volatile.Write(ref status, null);
            return;
        }
        var source = new CancellationTokenSource();
        request = source;
        var previous = worker;
        worker = Task.Run(async () =>
        {
            await previous;
            if (!source.IsCancellationRequested) await CaptureAsync(source.Token);
        });
    }

    public double? Read()
    {
        _ = GetWindowThreadProcessId(GetForegroundWindow(), out uint active);
        if (active == (uint)Environment.ProcessId) active = 0;
        lock (gate)
        {
            if (foreground != active)
            {
                Volatile.Write(ref foreground, active);
                chains.Clear();
            }
            if (chains.Count == 0) return null;
            var main = chains.Values.MaxBy(entry => entry.Count);
            chains.Clear();
            return 1000 * main.Count / main.Milliseconds;
        }
    }

    public void StopForExit()
    {
        SetEnabled(false);
        worker.Wait(TimeSpan.FromSeconds(2));
    }

    private async Task CaptureAsync(CancellationToken token)
    {
        if (!File.Exists(Executable))
        {
            Volatile.Write(ref status, "FPS requires the PresentMon helper included with the packaged app.");
            return;
        }
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            using var app = Process.GetCurrentProcess();
            string session = $"LlanoUltraControl-FPS-{identity.User!.Value}-{app.SessionId}" + (helperSession ? "-Helper" : "");
            using var binary = File.OpenRead(Executable);
            if (Convert.ToHexString(SHA256.HashData(binary)) != "B2A706BC6AD475749E3B7E3409263AA1E6906D45BDCF993F6DBC0F660188F1AF")
            {
                Volatile.Write(ref status, "The packaged PresentMon helper failed verification.");
                return;
            }
            Volatile.Write(ref status, null);
            using var process = Process.Start(Command("--session_name", session, "--stop_existing_session", "--output_stdout",
                "--no_console_stats", "--no_track_input", "--no_track_gpu", "--v2_metrics",
                "--exclude", StartupRegistration.LauncherName, "--exclude", StartupRegistration.AppHostName));
            if (process is null) return;
            var accessDenied = ReadAccessDeniedAsync(process.StandardError, token);
            try
            {
                PresentMonCsv format = default;
                while (await process.StandardOutput.ReadLineAsync(token) is { } line)
                {
                    if (line.StartsWith("Application,ProcessID,SwapChainAddress,", StringComparison.Ordinal))
                        PresentMonCsv.TryReadHeader(line, out format);
                    else
                        ReadFrame(line, format);
                }
                await process.WaitForExitAsync(token);
                Debug.WriteLine($"PresentMon exited with code {process.ExitCode}.");
                Volatile.Write(ref status, await accessDenied
                    ? StartupRegistration.IsElevated ? "Windows denied access to FPS capture."
                        : StartupRegistration.IsInstalled
                        ? "Windows denied FPS capture. Go to Settings → General and enable sensor and FPS access."
                        : "Windows denied FPS capture. Install the MSI to enable sensor and FPS access."
                    : "PresentMon capture stopped. Toggle FPS off and on to retry.");
            }
            finally
            {
                await StopCaptureAsync(process, session);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            Debug.WriteLine(error);
            Volatile.Write(ref status, "FPS capture couldn’t start.");
        }
    }

    private void ReadFrame(string line, PresentMonCsv format)
    {
        uint active = Volatile.Read(ref foreground);
        if (!format.TryReadFrame(line, active, out ulong chain, out double interval)) return;
        lock (gate)
        {
            if (active != foreground) return;
            var last = chains.GetValueOrDefault(chain);
            chains[chain] = (last.Count + 1, last.Milliseconds + interval);
        }
    }

    private static async Task<bool> ReadAccessDeniedAsync(StreamReader errors, CancellationToken token)
    {
        bool denied = false;
        try
        {
            while (await errors.ReadLineAsync(token) is { } line)
                denied |= line.Contains("access denied", StringComparison.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (IOException error) { Debug.WriteLine($"PresentMon diagnostics could not be read (0x{error.HResult:X8})."); }
        return denied;
    }

    private static async Task StopCaptureAsync(Process process, string session)
    {
        if (process.HasExited) return;
        Process? stop = null;
        using var limit = new CancellationTokenSource(TimeSpan.FromMilliseconds(900));
        try
        {
            stop = Process.Start(Command("--session_name", session, "--terminate_existing_session"));
            await process.WaitForExitAsync(limit.Token);
            if (stop is not null) await stop.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException) when (limit.IsCancellationRequested) { }
        finally
        {
            using (stop)
            {
                try { if (!process.HasExited) process.Kill(); }
                finally { if (stop is { HasExited: false }) stop.Kill(); }
            }
        }
    }

    private static ProcessStartInfo Command(params string[] arguments)
    {
        var start = new ProcessStartInfo(Executable)
        {
            WorkingDirectory = AppContext.BaseDirectory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
}
