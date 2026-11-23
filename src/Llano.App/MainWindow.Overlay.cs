using System.Windows;
using System.Windows.Controls;
using Llano.Core;

namespace Llano.App;

public partial class MainWindow
{
    private readonly Dictionary<StatsMetrics, CheckBox> overlayChoices = new();
    private StatsOverlayWindow? statsOverlay;
    private bool loadingOverlay;
    private readonly FpsCapture fpsCapture = new();

    private void InitializeOverlayPreferences()
    {
        OverlayOpacity.ItemsSource = new[] { 0, 25, 40, 55, 70, 85, 100 };
        for (int i = 0; i < StatsOverlayWindow.Fields.Length; i++)
        {
            var field = StatsOverlayWindow.Fields[i];
            var choice = new CheckBox { Content = field.Option, Margin = new Thickness(0, 0, 10, 7) };
            choice.Click += SaveOverlay;
            overlayChoices.Add(field.Metric, choice);
            OverlayFields.Children.Add(choice);
        }
        SetOverlayPreferences();
    }

    private void SetOverlayPreferences()
    {
        loadingOverlay = true;
        var options = settings.StatsOverlay;
        OverlayEnabled.IsChecked = options.Enabled;
        OverlayPosition.SelectedIndex = (int)options.Position;
        OverlaySize.SelectedIndex = (int)options.Size;
        OverlayLayout.SelectedIndex = options.Vertical ? 1 : 0;
        OverlayOpacity.SelectedItem = options.BackgroundOpacity;
        OverlayFollowTheme.IsChecked = options.FollowTheme;
        OverlayBorder.IsChecked = options.ShowBorder;
        foreach (var (metric, choice) in overlayChoices) choice.IsChecked = options.Metrics.HasFlag(metric);
        loadingOverlay = false;
    }

    private void SaveOverlay(object sender, RoutedEventArgs e)
    {
        if (!initialized || loadingOverlay || overlayChoices.Count == 0) return;
        StatsMetrics metrics = StatsMetrics.None;
        foreach (var (metric, choice) in overlayChoices)
            if (choice.IsChecked == true) metrics |= metric;
        try
        {
            var options = new StatsOverlaySettings
            {
                Enabled = OverlayEnabled.IsChecked == true, Metrics = metrics,
                Position = (StatsPosition)OverlayPosition.SelectedIndex, Size = (StatsSize)OverlaySize.SelectedIndex,
                Vertical = OverlayLayout.SelectedIndex == 1,
                FollowTheme = OverlayFollowTheme.IsChecked == true, ShowBorder = OverlayBorder.IsChecked == true,
                BackgroundOpacity = OverlayOpacity.SelectedItem is int opacity ? opacity : settings.StatsOverlay.BackgroundOpacity
            };
            Persist(settings with { StatsOverlay = options });
            OverlayStatus.Text = options.Enabled && metrics == StatsMetrics.None ? "Select at least one reading to show." : "";
            Refresh();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            SetOverlayPreferences();
            OverlayStatus.Text = "Couldn’t save the overlay. " + error.Message;
        }
    }

    private void UpdateStatsOverlay(int? rpm, SensorReading? cpu, SensorReading? gpu)
    {
        var options = settings.StatsOverlay;
        bool captureFps = WantsFps;
        fpsCapture.SetEnabled(captureFps && !UseTelemetryHelper);
        var helper = UseTelemetryHelper ? telemetry.Snapshot?.Reply : null;
        FpsStatus.Text = !captureFps ? "" : UseTelemetryHelper ? helper?.FpsStatus ?? telemetry.Status ?? "Starting the telemetry helper…" :
            fpsCapture.Status ?? "FPS follows the foreground app.";
        if (!options.Enabled || options.Metrics == StatsMetrics.None)
        {
            if (statsOverlay?.IsVisible == true) statsOverlay.Hide();
            return;
        }
        statsOverlay ??= new();
        statsOverlay.Configure(options, settings.Theme);
        statsOverlay.SetReading(StatsMetrics.Fps, !captureFps ? null : UseTelemetryHelper ? helper?.Fps : fpsCapture.Read());
        statsOverlay.SetReading(StatsMetrics.CoolerRpm, rpm);
        statsOverlay.SetReading(StatsMetrics.CpuTemperature, cpu?.Value);
        statsOverlay.SetReading(StatsMetrics.GpuTemperature, gpu?.Value);

        var gpuUsage = Find(Component.Gpu, "%", "GPU Core", gpu?.Hardware);
        string? gpuHardware = gpu?.Hardware ?? gpuUsage?.Hardware;
        statsOverlay.SetReading(StatsMetrics.CpuUsage, Find(Component.Cpu, "%", "CPU Total", cpu?.Hardware)?.Value);
        statsOverlay.SetReading(StatsMetrics.GpuUsage, gpuUsage?.Value);
        statsOverlay.SetReading(StatsMetrics.MemoryUsage, Find(Component.Memory, "%", "Memory")?.Value);
        if (options.Metrics.HasFlag(StatsMetrics.CpuClock))
        {
            double? cpuClock = ReadHardwareCpuClock(readings);
            if (!simulate && cpuClock is null) cpuClock = WindowsCpuClock.ReadMhz();
            statsOverlay.SetReading(StatsMetrics.CpuClock, cpuClock / 1000);
        }
        statsOverlay.SetReading(StatsMetrics.GpuClock, Find(Component.Gpu, "MHz", "GPU Core", gpuHardware)?.Value / 1000);
        if (!statsOverlay.IsVisible) statsOverlay.Show();
        statsOverlay.Position();

        SensorReading? Find(Component kind, string unit, string name, string? hardware = null) =>
            readings.Where(r => r.Component == kind && r.Unit == unit && r.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
                r.Value.HasValue && IsFresh(r) && (hardware is null || r.Hardware == hardware))
                .OrderBy(r => kind == Component.Gpu ? GraphicsPriority(r) : 0).FirstOrDefault();
    }

    private double? ReadHardwareCpuClock(IEnumerable<SensorReading> sensors)
    {
        var clocks = sensors.Where(r => r.Component == Component.Cpu && r.Unit == "MHz" && r.Value > 0 && IsFresh(r));
        double? average = clocks.FirstOrDefault(r => r.Name.Equals("Core Average", StringComparison.OrdinalIgnoreCase) ||
            r.Name.Equals("Average Core Clock", StringComparison.OrdinalIgnoreCase))?.Value;
        return average ?? clocks.Where(r => r.Name.StartsWith("CPU Core", StringComparison.OrdinalIgnoreCase) ||
            r.Name.StartsWith("Core #", StringComparison.OrdinalIgnoreCase)).Average(r => r.Value);
    }
}
