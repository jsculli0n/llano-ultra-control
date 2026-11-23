using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Security.Cryptography;

namespace Llano.App;

internal sealed class FpsCapture(bool helperSession = false)
{
    private static readonly string Executable = Path.Combine(AppContext.BaseDirectory, "PresentMon.exe");
    private readonly object gate = new();
    private readonly Dictionary<string, (int Count, double Milliseconds)> chains = new(StringComparer.Ordinal);
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
                foreground = active;
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
        using var identity = WindowsIdentity.GetCurrent();
        string session = "LlanoUltraControl-FPS-" + identity.User!.Value + (helperSession ? "-Helper" : "");
        try
        {
            using var binary = File.OpenRead(Executable);
            if (Convert.ToHexString(SHA256.HashData(binary)) != "B2A706BC6AD475749E3B7E3409263AA1E6906D45BDCF993F6DBC0F660188F1AF")
            {
                Volatile.Write(ref status, "The packaged PresentMon helper failed verification.");
                return;
            }
            Volatile.Write(ref status, null);
            using var process = Process.Start(Command("--session_name", session, "--stop_existing_session", "--output_stdout",
                "--no_console_stats", "--no_track_input", "--no_track_gpu", "--v2_metrics", "--exclude", "LlanoUltraControl.exe"));
            if (process is null) return;
            var errors = process.StandardError.ReadToEndAsync();
            try
            {
                int processColumn = -1, chainColumn = -1, timeColumn = -1;
                while (await process.StandardOutput.ReadLineAsync(token) is { } line)
                {
                    if (processColumn < 0 || line.StartsWith("Application,", StringComparison.Ordinal))
                    {
                        string[] fields = line.Split(',');
                        processColumn = Array.IndexOf(fields, "ProcessID");
                        chainColumn = Array.IndexOf(fields, "SwapChainAddress");
                        timeColumn = Array.IndexOf(fields, "DisplayedTime");
                    }
                    else if (chainColumn >= 0 && timeColumn >= 0)
                    {
                        ReadFrame(line, processColumn, chainColumn, timeColumn);
                    }
                }
                await process.WaitForExitAsync(token);
                string diagnostic = await errors;
                Debug.WriteLine(diagnostic);
                Volatile.Write(ref status, diagnostic.Contains("access denied", StringComparison.OrdinalIgnoreCase)
                    ? "Windows denied FPS capture. Restart the app as administrator from Settings → General."
                    : "PresentMon capture stopped. Toggle FPS off and on to retry.");
            }
            finally
            {
                if (!process.HasExited)
                {
                    using var stop = Process.Start(Command("--session_name", session, "--terminate_existing_session"));
                    using var limit = new CancellationTokenSource(TimeSpan.FromMilliseconds(900));
                    try { await process.WaitForExitAsync(limit.Token); }
                    catch (OperationCanceledException) { if (!process.HasExited) process.Kill(); }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException)
        {
            Debug.WriteLine(error);
            Volatile.Write(ref status, "FPS capture couldn’t start.");
        }
    }

    private void ReadFrame(string line, int processColumn, int chainColumn, int timeColumn)
    {
        var fields = line.Split(',');
        if (fields.Length <= Math.Max(processColumn, Math.Max(chainColumn, timeColumn)) ||
            !uint.TryParse(fields[processColumn], out uint processId) ||
            !double.TryParse(fields[timeColumn], CultureInfo.InvariantCulture, out double interval) ||
            !double.IsFinite(interval) || interval is <= 0 or > 2500) return;
        lock (gate)
        {
            if (processId != foreground) return;
            string chain = fields[chainColumn];
            var last = chains.GetValueOrDefault(chain);
            chains[chain] = (last.Count + 1, last.Milliseconds + interval);
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
