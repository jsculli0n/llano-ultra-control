using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using Llano.Core;

namespace Llano.App;

public partial class MainWindow
{
    private TelemetryClient telemetry = null!;
    private bool helperEnabled;
    private bool helperConfiguring;
    private bool UseTelemetryHelper => helperEnabled && !runningAsAdministrator && !simulate;
    private bool WantsFps => !simulate && settings.StatsOverlay.Enabled && settings.StatsOverlay.Metrics.HasFlag(StatsMetrics.Fps);

    private void InitializeTelemetryHelper()
    {
        telemetry = new(clock);
        try { helperEnabled = StartupRegistration.TelemetryEnabled(); }
        catch (Exception error) { Debug.WriteLine(error); }
        UpdateHelperStatus();
    }

    private void UpdateHelperStatus()
    {
        TelemetrySetup.IsEnabled = StartupRegistration.IsInstalled && !helperConfiguring;
        TelemetrySetup.Content = helperEnabled ? "Disable telemetry helper" : "Set up telemetry helper";
        TelemetryState.Text = !StartupRegistration.IsInstalled ? "Available with the MSI installation." :
            telemetry.Status ?? (helperEnabled ? "Authorized. Starts when readings need it." : "One Windows approval enables CPU readings and FPS on demand.");
    }

    private async void ConfigureTelemetry(object sender, RoutedEventArgs e)
    {
        if (helperConfiguring) return;
        helperConfiguring = true;
        UpdateHelperStatus();
        try
        {
            await StartupRegistration.SetTelemetryAsync(!helperEnabled);
            helperEnabled = StartupRegistration.TelemetryEnabled();
            if (helperEnabled && settings.RequestAdministratorOnLaunch)
            {
                Persist(settings with { RequestAdministratorOnLaunch = false });
                AdministratorLaunch.IsChecked = false;
            }
            TelemetryNotice.Text = "";
            Refresh();
        }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223)
        {
            TelemetryNotice.Text = "Administrator approval was cancelled.";
        }
        catch (Exception error)
        {
            Debug.WriteLine(error);
            TelemetryNotice.Text = "Couldn’t change telemetry access. Check Windows Task Scheduler and administrator permissions.";
        }
        finally
        {
            helperConfiguring = false;
            if (!closing) UpdateHelperStatus();
        }
    }

    private void UpdateTelemetry(SensorSnapshot local, TimeSpan now)
    {
        var request = TelemetryRequest.None;
        bool localHasCpu = local.TemperatureSensors.Any(r => r.Component == Component.Cpu && r.Value.HasValue &&
            TemperatureRank(r) < int.MaxValue && IsFresh(r));
        if (UseTelemetryHelper)
        {
            var overlay = settings.StatsOverlay;
            const StatsMetrics cpuMetrics = StatsMetrics.CpuTemperature | StatsMetrics.CpuClock | StatsMetrics.CpuUsage;
            bool showCpu = IsVisible && WindowState != WindowState.Minimized ||
                runningProfile is { Mode: not FanMode.Fixed } && controller.Enabled ||
                overlay.Enabled && (overlay.Metrics & cpuMetrics) != 0;
            if (showCpu && !localHasCpu) request |= TelemetryRequest.Cpu;
            if (WantsFps) request |= TelemetryRequest.Fps;
        }
        telemetry.Request(request);
        if (request.HasFlag(TelemetryRequest.Cpu) && telemetry.Snapshot is { } sampled)
        {
            var extra = sampled.Reply.Cpu
                .Where(r => r.Component == Component.Cpu && r.SampledAt >= TimeSpan.Zero)
                .Select(r => r with { SampledAt = sampled.ReceivedAt - r.SampledAt })
                .Where(r => now - r.SampledAt <= TimeSpan.FromSeconds(5) && r.SampledAt >= sensorValidAfter)
                .ToArray();
            var ids = extra.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
            readings = [.. readings.Where(r => !ids.Contains(r.Id)), ..extra];
            temperatureSensors = readings.Where(r => r.Unit == "°C").ToArray();
        }
        UpdateHelperStatus();
    }
}
