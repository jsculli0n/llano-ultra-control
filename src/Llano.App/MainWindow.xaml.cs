using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Llano.Core;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Llano.App;

public partial class MainWindow : Window
{
    private static readonly SpeedLimits CoolerLimits = V12UltraStatus.SpeedRange;
    private static readonly IReadOnlyDictionary<string, Temperature> NoTemperatures =
        new Dictionary<string, Temperature>();
    private const string DemoCpuId = "demo:cpu";
    private const string DemoGpuId = "demo:gpu";

    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly CancellationTokenSource lifetime = new();
    private readonly FanController controller = new(CoolerLimits);
    private readonly CoolerDiscovery coolerDiscovery = new();
    private readonly object usbWriteGate = new();
    private readonly bool runningAsAdministrator = StartupRegistration.IsElevated;
    private readonly SettingsStore store;
    private readonly SensorMonitor monitor;
    private readonly DispatcherTimer timer;
    private readonly Forms.NotifyIcon tray;
    private readonly Forms.ToolStripMenuItem pauseMenu;
    private readonly Forms.ToolStripMenuItem resumeMenu;
    private AppSettings settings;
    private FanProfile? runningProfile;
    private CoolingMode? activeCoolingMode;
    private FanDecision lastDecision;
    private string? controlledDevice;
    private bool driveHardware;
    private int controlGeneration;
    private Lighting? pendingLighting;
    private SensorReading[] readings = [];
    private SensorReading[] temperatureSensors = [];
    private TimeSpan readingTime;
    private SensorRow[] displayedRows = [];
    private HidCooler? cooler;
    private V12UltraStatus? coolerStatus;
    private string hidSummary = "Checking for a V12 Ultra.";
    private string? message;
    private bool initialized;
    private bool loading;
    private bool simulate;
    private bool scanning;
    private bool closing;
    private TimeSpan sensorValidAfter = TimeSpan.MinValue;
    private string[] sensorKeys = [];
    private CancellationTokenSource? capture;

    internal MainWindow(bool trayLaunch, bool demo, SettingsStore store, AppSettings settings, string? startupNotice)
    {
        this.store = store;
        this.settings = settings;
        InitializeComponent();
        SensorElevateButton.Visibility = VisibilityFor(!runningAsAdministrator);
        CpuAccessHelp.Text = runningAsAdministrator
            ? LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled
                ? "Administrator access is active and a PawnIO installation was detected. HWiNFO with Shared Memory Support is another temperature source."
                : "Administrator access is active. LibreHardwareMonitor did not detect a standalone PawnIO installation. Some CPUs need PawnIO or HWiNFO Shared Memory Support."
            : "This app has standard permissions. Restart it with administrator access to try reading the CPU temperature.";
        AppPalette.Apply(settings.Theme);

        monitor = new(clock);
        timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => Refresh();
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => ShowDashboard());
        pauseMenu = new("Pause fan control", null, (_, _) => PauseControl());
        resumeMenu = new("Resume fan control", null, (_, _) => ResumeControl());
        menu.Items.Add(pauseMenu);
        menu.Items.Add(resumeMenu);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Exit());
        tray = new()
        {
            Text = "Llano Ultra Control",
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ??
                (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone(),
            ContextMenuStrip = menu,
            Visible = true
        };
        Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(tray.Icon.Handle,
            Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
        tray.DoubleClick += (_, _) => ShowDashboard();
        IsVisibleChanged += (_, _) => UpdateVisiblePage();
        StateChanged += (_, _) =>
        {
            if (initialized) UpdateLighting();
        };
        Closing += WindowClosing;
        Application.Current.Exit += ApplicationExiting;
        Application.Current.SessionEnding += WindowsSessionEnding;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        initialized = true;
        InitializePreferences();
        ComponentFilter.SelectedIndex = 0;
        ReloadProfiles();
        LoadCoolingChoice();
        InitializeLighting();
        if (demo) DemoToggle.IsChecked = true;
        else ChangeSimulation(this, null!);
        message = startupNotice;
        LoadStartupState();
        PowerOffAtShutdown.IsChecked = settings.PowerOffAtShutdown;
        ChangeFanMode(this, null!);
        if (trayLaunch && settings.ResumeAtSignIn && !simulate)
        {
            var saved = settings.LastCoolingMode.HasValue ? CoolingProfile() :
                settings.Profiles.First(p => p.Name == settings.SelectedProfile);
            if (saved.Validate(CoolerLimits, requireSensors: true) is { } validation) message ??= validation;
            else
            {
                driveHardware = true;
                runningProfile = saved;
                activeCoolingMode = settings.LastCoolingMode;
                controller.Start(saved);
            }
        }
        _ = Task.Run(() => monitor.RunAsync(lifetime.Token));
        timer.Start();
        Refresh();
    }

    private void Refresh()
    {
        var live = monitor.Snapshot;
        TimeSpan now = clock.Elapsed;
        readingTime = now;
        if (simulate)
        {
            readings = [
                new(DemoCpuId, Component.Cpu, "Simulated CPU", "Package", "°C",
                    DemoSensorLoss.IsChecked == true ? null : DemoCpu.Value, now),
                new(DemoGpuId, Component.Gpu, "Simulated GPU", "Temperature", "°C",
                    DemoGpuLoss.IsChecked == true ? null : DemoGpu.Value, now)
            ];
            temperatureSensors = readings;
            DemoCpuLabel.Text = $"Simulated CPU temperature  ·  {DemoCpu.Value:0}°C";
            DemoGpuLabel.Text = $"Simulated GPU temperature  ·  {DemoGpu.Value:0}°C";
        }
        else
        {
            readings = live.Readings;
            temperatureSensors = live.TemperatureSensors;
        }
        var cpu = PreferredTemperature(Component.Cpu);
        var gpu = PreferredTemperature(Component.Gpu);
        hasUsableCpuTemperature = cpu is { Value: double cpuTemperature } &&
            new Temperature(cpuTemperature, cpu.SampledAt).IsFresh(now);
        CpuTile.Text = cpu?.Display ?? "Unavailable";
        GpuTile.Text = gpu?.Display ?? "Unavailable";
        CpuSource.Text = cpu?.Name ?? "";
        GpuSource.Text = gpu?.Hardware ?? "";
        CpuTile.ToolTip = cpu?.Caption;
        GpuTile.ToolTip = gpu?.Caption;
        UpdateSensorSummaries(cpu, gpu, live != SensorSnapshot.Empty);
        SensorSource.Text = simulate ? "Simulated CPU and GPU inputs" : live.Status;
        UpdateSensorSelectors();
        if (IsVisible && Navigation.SelectedItem == SensorsPage) UpdateReadingsGrid();

        var decision = controller.Update(now, CurrentTemperatures(cpu, gpu),
            connected: !driveHardware || coolerStatus is { Powered: true },
            currentRpm: simulate ? lastDecision.TargetRpm :
                driveHardware && coolerStatus is { Powered: true } status ? status.SettingRpm : null);
        lastDecision = decision;
        UpdateCoolingDashboard(now, decision);
        bool automatic = runningProfile?.Mode == FanMode.Automatic;
        string rpm = decision.TargetRpm is { } target ? $"{target:N0} RPM" : "—";
        string action = driveHardware ? "" : " preview";
        FanStatus.Text = decision.State switch
        {
            FanState.WaitingForCooler => coolerStatus is { Powered: false } ? "Cooler is off. Turn it on in Settings." :
                cooler is null ? "Cooler USB connection lost. Check the data cable." : "Waiting for the cooler to respond over USB.",
            FanState.WaitingForSensors => automatic ? "Waiting for a CPU temperature." : "Waiting for all selected temperatures.",
            FanState.SensorFallback => automatic ? $"CPU temperature lost. Using the preset maximum of {rpm}." :
                $"A selected sensor is unavailable. Profile maximum {rpm}.",
            FanState.Fixed => $"{runningProfile?.Name}  ·  Fixed speed{action}  ·  {rpm}",
            FanState.Smart => $"{runningProfile?.Name}  ·  {(automatic ? "Automatic" : "Custom")}{action}  ·  {rpm}",
            FanState.DialOverride => "The physical dial is in control. Apply a cooling mode to resume.",
            _ => "Cooling paused"
        };
        FanStatus.Foreground = (Brush)FindResource(decision.State == FanState.SensorFallback ? "Warning" : "Muted");
        pauseMenu.Enabled = controller.Enabled;
        resumeMenu.Enabled = !controller.Enabled && runningProfile is not null;
        UpdateControlAvailability();
        tray.Text = decision.State == FanState.SensorFallback ? "Llano Ultra Control · Sensor unavailable" :
            controller.Enabled ? $"Llano Ultra Control · {rpm}" : "Llano Ultra Control";
        Footer.Text = message ?? "";
        if (!scanning && !powerChanging && !closing) _ = ScanCoolerAsync(driveHardware ? decision.TargetRpm : null);
    }

    private void UpdateControlAvailability()
    {
        bool available = coolerStatus is { Powered: true };
        ConnectionMode.Text = simulate ? "Simulation" : available ? "Connected · On" :
            coolerStatus is { Powered: false } ? "Connected · Off" : cooler is not null ? "USB response pending" : "USB disconnected";
        UpdateCoolingActions();
        PauseButton.Visibility = VisibilityFor(controller.Enabled);
        UpdateLightingActions();
        CaptureButton.IsEnabled = capture is not null || cooler?.Identity.Input > 0;
        UpdatePowerControls();
        UpdateRecoveryHelp();
        LightingDeviceStatus.Text = simulate ? "Simulation is active." : coolerStatus switch
        {
            { Powered: true, LightingEnabled: true } status => $"Cooler brightness {status.BrightnessPercent:N0}%",
            { Powered: true } => "Cooler lighting off",
            { Powered: false } => "Cooler powered off",
            _ => "Waiting for cooler status…"
        };
    }

    private void ResumeControl()
    {
        if (runningProfile is null) return;
        Interlocked.Increment(ref controlGeneration);
        controlledDevice = null;
        controller.Start(runningProfile);
        message = null;
        Refresh();
    }

    private void PauseFan(object sender, RoutedEventArgs e) => PauseControl();

    private void PauseControl(bool refresh = true)
    {
        Interlocked.Increment(ref controlGeneration);
        controlledDevice = null;
        pendingLighting = null;
        controller.Pause();
        if (refresh) Refresh();
    }

    private void ChangePage(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.Source, Navigation)) UpdateVisiblePage();
    }

    private void UpdateVisiblePage()
    {
        if (!initialized) return;
        if (IsVisible && Navigation.SelectedItem == SensorsPage) UpdateReadingsGrid();
        UpdateControlAvailability();
        UpdateLighting();
    }

    private void ChangeSimulation(object sender, RoutedEventArgs e)
    {
        if (!initialized) return;
        bool wasSimulating = simulate;
        simulate = DemoToggle.IsChecked == true;
        runningProfile = null;
        activeCoolingMode = null;
        if (wasSimulating != simulate) SpeedHistory.Clear();
        driveHardware = false;
        PauseControl(refresh: false);
        message = null;
        SimulationInputs.Visibility = VisibilityFor(simulate);
        if (wasSimulating && !simulate && ProfilesList.SelectedItem is string name)
        {
            SharedSensors.UnselectAll();
            CpuSensor.SelectedIndex = GpuSensor.SelectedIndex = -1;
            Refresh();
            var saved = settings.Profiles.First(p => p.Name == name);
            UpdateSensorSelectors(saved.SharedSensors, saved.CpuSensor, saved.GpuSensor, force: true);
        }
        else Refresh();
    }

    private void LoadStartupState()
    {
        bool installed = StartupRegistration.IsInstalled;
        ElevateButton.IsEnabled = !runningAsAdministrator;
        AppPermissionStatus.Text = runningAsAdministrator
            ? "App permissions: Administrator"
            : "App permissions: Standard";
        StartupMode.IsEnabled = ResumeAtSignIn.IsEnabled = StartupApply.IsEnabled = installed;
        ResumeAtSignIn.IsChecked = settings.ResumeAtSignIn;
        try
        {
            StartupMode.SelectedIndex = installed ? (int)StartupRegistration.GetMode() : 0;
            StartupStatus.Text = installed ? "" : "Sign-in options are available with the MSI installation.";
        }
        catch (Exception error)
        {
            Debug.WriteLine(error);
            StartupStatus.Text = "Windows Task Scheduler is unavailable. Try again after it reconnects.";
        }
    }

    private async void SaveStartup(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        var mode = (Llano.App.StartupMode)StartupMode.SelectedIndex;
        bool resume = ResumeAtSignIn.IsChecked == true;
        button.IsEnabled = false;
        StartupMode.IsEnabled = ResumeAtSignIn.IsEnabled = false;
        try
        {
            await StartupRegistration.SetAsync(mode);
            Persist(settings with
            {
                ResumeAtSignIn = resume,
                SelectedProfile = ProfilesList.SelectedItem as string ?? settings.SelectedProfile
            });
            StartupStatus.Text = "Startup settings saved.";
        }
        catch (Exception error) { StartupStatus.Text = error.Message; }
        finally { button.IsEnabled = StartupMode.IsEnabled = ResumeAtSignIn.IsEnabled = true; }
    }

    private void Elevate(object sender, RoutedEventArgs e)
    {
        bool fromSensors = sender == SensorElevateButton;
        try { App.RestartElevated(simulate, fromSensors); }
        catch (Win32Exception error)
        {
            string notice = error.NativeErrorCode == 1223 ? "Administrator approval was cancelled." : error.Message;
            if (fromSensors) ShowMessage(notice);
            else AdministratorNotice.Text = notice;
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        _ = Dispatcher.InvokeAsync(() =>
        {
            Interlocked.Increment(ref controlGeneration);
            controlledDevice = null;
            sensorValidAfter = clock.Elapsed;
            controller.InvalidateConnection();
            Refresh();
        });
    }

    private void ShowMessage(string text) => Footer.Text = message = text;
    internal void ShowDashboard()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (closing) return;
        e.Cancel = true;
        if (settings.CloseToTray) Hide();
        else _ = Dispatcher.BeginInvoke(Exit);
    }

    private void ExitClick(object sender, RoutedEventArgs e) => Exit();
    private void Exit()
    {
        closing = true;
        Application.Current.Shutdown();
    }

    private void ApplicationExiting(object sender, ExitEventArgs e)
    {
        closing = true;
        Interlocked.Increment(ref controlGeneration);
        timer.Stop();
        lifetime.Cancel();
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        Application.Current.SessionEnding -= WindowsSessionEnding;
        tray.Visible = false;
        tray.ContextMenuStrip?.Dispose();
        tray.Icon?.Dispose();
        tray.Dispose();
    }
}
