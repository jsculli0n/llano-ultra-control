using System.Diagnostics;
using System.Security.Principal;
using System.Windows;

namespace Llano.App;

public partial class App : Application
{
    private Mutex? instance;
    private EventWaitHandle? showRequest;
    private RegisteredWaitHandle? showListener;
    private bool holdsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 1 && e.Args[0] == "--remove-startup-tasks")
        {
            Shutdown(StartupRegistration.RemoveInstalledTasks() ? 0 : 1);
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--configure-startup")
        {
            Shutdown(StartupRegistration.ApplyElevatedCommand(e.Args[1]) ? 0 : 1);
            return;
        }

        string userSid = WindowsIdentity.GetCurrent().User!.Value;
        showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\LlanoUltraControl-Show-{userSid}", out bool newShowRequest);
        instance = new Mutex(false, $@"Local\LlanoUltraControl-{userSid}");
        try { holdsMutex = instance.WaitOne(e.Args.Contains("--restart") ? TimeSpan.FromSeconds(10) : TimeSpan.Zero); }
        catch (AbandonedMutexException) { holdsMutex = true; }
        if (!holdsMutex)
        {
            if (!e.Args.Contains("--tray"))
            {
                if (newShowRequest)
                    MessageBox.Show("Exit the earlier version from its notification area icon, then open this version.",
                        "Llano Ultra Control");
                else
                    showRequest.Set();
            }
            Shutdown();
            return;
        }

        var window = new MainWindow(e.Args.Contains("--tray"), e.Args.Contains("--demo"));
        MainWindow = window;
        showListener = ThreadPool.RegisterWaitForSingleObject(showRequest,
            (_, _) => Dispatcher.InvokeAsync(window.ShowDashboard), null, Timeout.Infinite, executeOnlyOnce: false);
        if (e.Args.Contains("--sensors")) window.ShowSensors();
        if (!e.Args.Contains("--tray")) window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        showListener?.Unregister(null);
        showRequest?.Dispose();
        if (holdsMutex) instance?.ReleaseMutex();
        instance?.Dispose();
        base.OnExit(e);
    }

    public static void RestartElevated(bool demo, bool showSensors)
    {
        string path = Environment.ProcessPath!;
        var start = new ProcessStartInfo(path)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(path)!
        };
        start.ArgumentList.Add("--restart");
        if (demo) start.ArgumentList.Add("--demo");
        if (showSensors) start.ArgumentList.Add("--sensors");
        using var process = Process.Start(start);
        if (process is not null) Current.Shutdown();
    }
}
