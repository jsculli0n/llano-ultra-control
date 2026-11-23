using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using Llano.Core;

namespace Llano.App;

public partial class MainWindow
{
    private bool powerChanging;
    private SignInPowerOn? signInPower;

    private void SaveShutdownPower(object sender, RoutedEventArgs e)
    {
        try
        {
            Persist(settings with { PowerOffAtShutdown = PowerOffAtShutdown.IsChecked == true });
            if (!settings.PowerOffAtShutdown) signInPower = null;
            PowerMessage.Text = "Power preference saved.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            PowerOffAtShutdown.IsChecked = settings.PowerOffAtShutdown;
            PowerMessage.Text = error.Message;
        }
    }

    private void UpdatePowerControls()
    {
        PowerButton.IsEnabled = !powerChanging && !simulate && cooler is not null && coolerStatus is not null;
        PowerButton.Content = powerChanging ? "Changing power…" : coolerStatus switch
        {
            { Powered: false } => "Turn cooler on",
            { Powered: true } => "Turn cooler off",
            _ => "Power unavailable"
        };
        CoolerPowerStatus.Text = simulate ? "Simulation" : coolerStatus switch
        {
            { Powered: true } => "Cooler is on",
            { Powered: false } => "Cooler is off",
            _ => cooler is null ? "Connect the cooler by USB" : "Waiting for a USB reply"
        };
    }

    private async void ToggleCoolerPower(object sender, RoutedEventArgs e)
    {
        if (powerChanging || simulate || cooler is not { } expected || coolerStatus is not { } status) return;
        signInPower = null;
        await ChangePowerAsync(expected, !status.Powered);
    }

    private void TryPowerOnAtSignIn(TimeSpan now)
    {
        if (signInPower is not { } request || simulate || closing || powerChanging || scanning) return;
        bool attempt = request.TryAttempt(now, coolerStatus?.Powered);
        if (!request.Pending) signInPower = null;
        if (attempt && cooler is { } expected)
            _ = ChangePowerAsync(expected, turnOn: true, atSignIn: true);
    }

    private async Task ChangePowerAsync(HidCooler expected, bool turnOn, bool atSignIn = false)
    {
        powerChanging = true;
        if (!turnOn) PauseControl(refresh: false);
        else Interlocked.Increment(ref controlGeneration);
        int generation = Volatile.Read(ref controlGeneration);
        bool IsCurrent() => generation == Volatile.Read(ref controlGeneration);
        UpdateControlAvailability();
        PowerMessage.Text = atSignIn ? "Turning cooler on at sign-in…" : turnOn ? "Turning cooler on…" : "Turning cooler off…";
        try
        {
            var updated = await Task.Run(() => ChangeDevicePower(expected, turnOn, IsCurrent));
            if (updated is null || !IsCurrent() || closing) return;
            coolerStatus = updated;
            if (turnOn)
            {
                signInPower = null;
                sensorValidAfter = clock.Elapsed;
                controller.InvalidateConnection();
            }
            PowerMessage.Text = turnOn ? "Cooler is on." : "Cooler is off.";
        }
        catch (Exception error) when (error is IOException or Win32Exception)
        {
            PowerMessage.Text = atSignIn ? $"Couldn’t turn on the cooler at sign-in. {error.Message}" : error.Message;
        }
        finally
        {
            powerChanging = false;
            if (!closing) Refresh();
        }
    }

    private V12UltraStatus? ChangeDevicePower(HidCooler expected, bool powered, Func<bool> isCurrent, bool verify = true)
    {
        lock (usbWriteGate)
        {
            if (!isCurrent()) return null;
            var devices = HidCooler.Find();
            if (devices.Count != 1 || devices[0].DevicePath != expected.DevicePath ||
                devices[0].ReadStatus() is not { } observed)
                throw new IOException("Connect one V12 Ultra over USB to change its power.");
            return observed.Powered == powered ? observed : devices[0].SetPower(observed, powered, isCurrent, verify);
        }
    }

    private void WindowsSessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        if (e.ReasonSessionEnding != ReasonSessionEnding.Shutdown || !settings.PowerOffAtShutdown ||
            simulate || closing || cooler is not { } expected) return;

        Interlocked.Increment(ref controlGeneration);
        var budget = Stopwatch.StartNew();
        TimeSpan limit = TimeSpan.FromMilliseconds(450);
        bool HasTime() => budget.Elapsed < limit;
        var send = Task.Run(() =>
        {
            try { ChangeDevicePower(expected, false, HasTime, verify: false); }
            catch (Exception error) when (error is IOException or Win32Exception) { Debug.WriteLine(error.Message); }
        });
        // Keep the session-ending callback brief. USB may already have been suspended by Windows.
        send.Wait(limit);
    }
}
