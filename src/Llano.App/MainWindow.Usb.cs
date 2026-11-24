using System.ComponentModel;
using System.Windows;
using Llano.Core;

namespace Llano.App;

public partial class MainWindow
{
    private async Task ScanCoolerAsync(int? rpm)
    {
        scanning = true;
        string? expected = cooler?.DevicePath;
        string? owned = controlledDevice;
        var lighting = pendingLighting;
        pendingLighting = null;
        lightingInFlight = lighting is not null;
        // Keep a failed lighting write from pausing an active cooling session.
        if (lightingInFlight) rpm = null;
        int generation = Volatile.Read(ref controlGeneration);
        bool IsCurrent() => generation == Volatile.Read(ref controlGeneration);
        bool attemptedWrite = false;
        try
        {
            var (devices, status, overrideDetected, writeConfirmed) = await Task.Run(() =>
            {
                var (devices, observed) = coolerDiscovery.Poll(clock.Elapsed);
                if (devices.Count != 1) return (devices, observed, false, false);
                var found = devices[0];
                if (observed is null || found.DevicePath != expected || !IsCurrent()) return (devices, observed, false, false);
                if (observed is { Powered: true, SoftwareControl: false } && found.DevicePath == owned)
                    return (devices, observed, true, false);
                if (!observed.Powered || (lighting is null && (rpm is null || observed.SoftwareControl && observed.SettingRpm == rpm)))
                    return (devices, observed, false, false);
                (devices, observed) = coolerDiscovery.Poll(clock.Elapsed, force: true);
                if (devices.Count != 1 || devices[0].DevicePath != expected || observed is not { Powered: true } || !IsCurrent())
                    return (devices, observed, false, false);
                found = devices[0];
                if (!observed.SoftwareControl && found.DevicePath == owned) return (devices, observed, true, false);
                try
                {
                    lock (usbWriteGate)
                    {
                        if (!IsCurrent()) return (devices, observed, false, false);
                        attemptedWrite = true;
                        var applied = found.Apply(observed, rpm, lighting, IsCurrent);
                        return (devices, applied, false, applied is not null);
                    }
                }
                catch (Exception error) when (error is IOException or Win32Exception)
                {
                    if (!IsCurrent()) return (devices, observed, false, false);
                    (devices, observed) = coolerDiscovery.Poll(clock.Elapsed, force: true);
                    if (devices.Count != 1 || devices[0].DevicePath != expected || observed is not { Powered: true })
                        return (devices, observed, false, false);
                    if (!observed.SoftwareControl && found.DevicePath == owned) return (devices, observed, true, false);
                    throw;
                }
            });
            if (closing || powerChanging || !IsCurrent()) return;
            if (driveHardware && status is { Powered: true } &&
                (coolerStatus is not { Powered: true } || cooler?.DevicePath != devices[0].DevicePath))
            {
                sensorValidAfter = clock.Elapsed;
                controller.InvalidateConnection();
            }
            cooler = devices.Count == 1 ? devices[0] : null;
            coolerStatus = status;
            if (status is not { Powered: true } || controlledDevice != cooler?.DevicePath) controlledDevice = null;
            if (overrideDetected && IsCurrent())
            {
                controlledDevice = null;
                controller.Update(clock.Elapsed, NoTemperatures, connected: true, physicalOverride: true);
                pendingLighting ??= lighting;
            }
            else if (writeConfirmed)
            {
                if (rpm.HasValue) controlledDevice = cooler?.DevicePath;
                if (lighting is not null) RememberLighting(lighting);
            }
            else if (lighting is not null && IsCurrent()) ShowMessage("The cooler was unavailable. Apply the lighting settings again.");
            if (IsCurrent() && rpm.HasValue && status is { SoftwareControl: true } && cooler?.DevicePath == expected)
                controlledDevice = cooler?.DevicePath;
            hidSummary = devices.Count switch
            {
                0 => "V12 Ultra USB device unavailable. Check power and the USB data cable.",
                1 => cooler!.Identity.ToString(),
                _ => $"Windows reports {devices.Count} matching HID interfaces. Connect one cooler for diagnostics."
            };
            DeviceState.Text = devices.Count == 1 ? "V12 Ultra detected" : hidSummary;
            HidDetails.Text = status is null ? hidSummary :
                $"{hidSummary}\nReported setting {status.SettingRpm:N0} RPM  ·  {(status.SoftwareControl ? "Software control" : "Physical dial")}";
            UpdateControlAvailability();
        }
        catch (Exception error)
        {
            if (closing || powerChanging || !IsCurrent()) return;
            if (attemptedWrite)
            {
                if (rpm.HasValue) PauseControl();
                ShowMessage($"Couldn’t apply the cooler settings. {error.Message}");
                return;
            }
            cooler = null;
            coolerStatus = null;
            hidSummary = $"USB detection failed. {error.Message}";
            DeviceState.Text = hidSummary;
            HidDetails.Text = hidSummary;
            UpdateControlAvailability();
        }
        finally
        {
            scanning = false;
            lightingInFlight = false;
            if (!closing)
            {
                UpdateLightingActions();
                if (lighting is not null && driveHardware && controller.Enabled && IsCurrent() && !powerChanging)
                    Refresh();
            }
        }
    }

    private async void CaptureInput(object sender, RoutedEventArgs e)
    {
        if (capture is not null)
        {
            capture.Cancel();
            return;
        }
        if (cooler is not { } device) return;
        using var source = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        capture = source;
        CaptureButton.Content = "_Stop listening";
        CaptureLog.Text = "Listening for 15 seconds. Move the speed dial and press the RGB mode button.";
        try { CaptureLog.Text = await Task.Run(() => device.CaptureAsync(source.Token)); }
        catch (Exception error) { CaptureLog.Text = $"Unable to listen for HID input. {error.Message}"; }
        finally
        {
            capture = null;
            CaptureButton.Content = "_Listen for HID input";
        }
    }

    private void CopyDiagnostics(object sender, RoutedEventArgs e)
    {
        try
        {
            string historyStatus = historySaveFailed ? "\nCooling history could not be saved. Check disk space and app data permissions." :
                historyReadFailed ? "\nCooling history could not be opened. Check app data permissions." : "";
            Clipboard.SetText($"Llano Ultra Control {ReleaseVersion}\n{HidDetails.Text}\n{SensorSource.Text}{historyStatus}\n{CaptureLog.Text}".TrimEnd());
            ShowMessage("Copied USB and sensor diagnostics.");
        }
        catch (System.Runtime.InteropServices.ExternalException) { ShowMessage("Clipboard busy. Try copying again."); }
    }
}
