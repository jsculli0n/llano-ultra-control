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
        TelemetrySetup.Content = helperEnabled ? "Disable sensor and FPS access" : "Enable sensor and FPS access";
        TelemetryState.Text = !StartupRegistration.IsInstalled ? "Install the MSI to enable background sensor and FPS access." :
            telemetry.Status ?? (helperEnabled ? "Enabled. CPU readings and FPS are requested as needed." :
                "Approve once in Windows to enable CPU temperature, CPU clock and FPS on demand.");
        SensorAccessSettings.Visibility = VisibilityFor(!runningAsAdministrator && !helperEnabled);
        CpuAccessHelp.Text = runningAsAdministrator || helperEnabled
            ? "CPU temperature depends on hardware and driver support. Check Settings → Diagnostics for your current sensor source."
            : StartupRegistration.IsInstalled ? "Enable sensor and FPS access in Settings to try reading the CPU temperature."
                : "Install the MSI to enable background CPU sensor access.";
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
            TelemetryNotice.Text = "";
            LoadStartupState();
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

    private void OpenSensorAccessSettings(object sender, RoutedEventArgs e)
    {
        Navigation.SelectedItem = SettingsPage;
        SettingsSections.SelectedIndex = 0;
        TelemetrySetup.Focus();
        TelemetrySetup.BringIntoView();
    }

    private bool UpdateTelemetry(SensorSnapshot local, TimeSpan now)
    {
        var request = TelemetryRequest.None;
        if (UseTelemetryHelper)
        {
            bool localHasTemperature = local.TemperatureSensors.Any(r => r.Component == Component.Cpu &&
                TemperatureRank(r) < int.MaxValue && TemperatureIsFresh(r));
            var overlay = settings.StatsOverlay;
            bool needTemperature = IsVisible && WindowState != WindowState.Minimized ||
                runningProfile is { Mode: not FanMode.Fixed } && controller.Enabled ||
                overlay.Enabled && overlay.Metrics.HasFlag(StatsMetrics.CpuTemperature);
            bool needClock = overlay.Enabled && overlay.Metrics.HasFlag(StatsMetrics.CpuClock);
            bool missingProfileCpu = controller.Enabled && runningProfile is { Mode: FanMode.SharedCurve or FanMode.SeparateCurves } custom &&
                custom.ActiveSensors.Any(id => IsHardwareCpuSensor(id) &&
                    !local.TemperatureSensors.Any(r => r.Id == id && TemperatureIsFresh(r)));
            if (missingProfileCpu || needTemperature && !localHasTemperature ||
                needClock && ReadHardwareCpuClock(local.Readings) is null)
                request |= TelemetryRequest.Cpu;
            if (WantsFps) request |= TelemetryRequest.Fps;
        }
        telemetry.Request(request);
        bool usingHelperReadings = false;
        if (request.HasFlag(TelemetryRequest.Cpu) && telemetry.Snapshot is { } sampled && sampled.ReceivedAt <= now)
        {
            var extra = sampled.Reply.Cpu
                .Where(r => r.SampledAt <= TimeSpan.FromSeconds(5) && (r.Unit != "°C" || r.Value is >= -40 and <= 150))
                .Select(r => r with { SampledAt = sampled.ReceivedAt - r.SampledAt })
                .Where(IsFresh)
                .ToArray();
            if (extra.Length > 0)
            {
                var ids = extra.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
                readings = [.. readings.Where(r => !ids.Contains(r.Id)), ..extra];
                temperatureSensors = readings.Where(r => r.Unit == "°C").ToArray();
                usingHelperReadings = true;
            }
        }
        UpdateHelperStatus();
        return usingHelperReadings;
    }

    private static bool IsHardwareCpuSensor(string id) =>
        id.StartsWith("lhm:/intelcpu/", StringComparison.Ordinal) ||
        id.StartsWith("lhm:/amdcpu/", StringComparison.Ordinal) ||
        id.StartsWith("lhm:/genericcpu/", StringComparison.Ordinal);
}
