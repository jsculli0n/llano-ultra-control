using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using System.Windows;
using Llano.Core;

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
        if (e.Args.Length == 3 && e.Args[0] == "--configure-telemetry")
        {
            Shutdown(StartupRegistration.ApplyTelemetryCommand(e.Args[1], e.Args[2]) ? 0 : 1);
            return;
        }
        if (e.Args.Length == 1 && e.Args[0] == StartupRegistration.TelemetryArgument)
        {
            RunTelemetryHelper();
            return;
        }

        using var identity = WindowsIdentity.GetCurrent();
        string userSid = identity.User!.Value;
        bool trayLaunch = e.Args.Contains("--tray");
        bool demo = e.Args.Contains("--demo");
        bool restarting = e.Args.Contains("--restart");
        bool showSensors = e.Args.Contains("--sensors");
        showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\LlanoUltraControl-Show-{userSid}", out bool newShowRequest);
        instance = new Mutex(false, $@"Local\LlanoUltraControl-{userSid}");
        try { holdsMutex = instance.WaitOne(restarting ? TimeSpan.FromSeconds(10) : TimeSpan.Zero); }
        catch (AbandonedMutexException) { holdsMutex = true; }
        if (!holdsMutex)
        {
            if (!trayLaunch)
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

        var store = new SettingsStore(AppDataFiles.SettingsPath, V12UltraStatus.SpeedRange);
        AppSettings settings;
        string? startupNotice = null;
        try { settings = store.Load(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            settings = new();
            startupNotice = $"Unable to load saved profiles. {error.Message}";
        }

        if (trayLaunch && StartupRegistration.IsInstalled && StartupRegistration.IsElevated)
            StartupRegistration.TryMigrateElevatedStartup();

        var window = new MainWindow(trayLaunch, demo, store, settings, startupNotice);
        MainWindow = window;
        showListener = ThreadPool.RegisterWaitForSingleObject(showRequest,
            (_, _) => Dispatcher.InvokeAsync(window.ShowDashboard), null, Timeout.Infinite, executeOnlyOnce: false);
        if (showSensors) window.ShowSensors();
        if (!trayLaunch) window.Show();
    }

    private async void RunTelemetryHelper()
    {
        int result = 1;
        try
        {
            if (StartupRegistration.IsInstalled && StartupRegistration.IsElevated)
            {
                await TelemetryHelper.RunAsync();
                result = 0;
            }
        }
        catch (Exception error) { Debug.WriteLine(error); }
        Shutdown(result);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        showListener?.Unregister(null);
        showRequest?.Dispose();
        if (holdsMutex) instance?.ReleaseMutex();
        instance?.Dispose();
        base.OnExit(e);
    }

}
