using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Llano.Core;

namespace Llano.App;

public partial class MainWindow
{
    internal static string ReleaseVersion => typeof(MainWindow).Assembly.GetName().Version!.ToString(3);
    private Version? availableUpdate;

    private void InitializePreferences()
    {
        ThemePicker.SelectedIndex = (int)settings.Theme;
        CloseToTray.IsChecked = settings.CloseToTray;
        AdministratorLaunch.IsChecked = settings.RequestAdministratorOnLaunch;
        InitializeOverlayPreferences();
        AboutVersion.Text = $"Version {ReleaseVersion}  ·  {(StartupRegistration.IsInstalled ? "Installed" : "Portable")}";
        foreach (var button in new[] { OpenGuide, OpenLicence, OpenNotices })
            button.IsEnabled = File.Exists(Path.Combine(AppContext.BaseDirectory, (string)button.Tag));
    }

    private void ChangeTheme(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized || ThemePicker.SelectedIndex < 0) return;
        var selected = (AppTheme)ThemePicker.SelectedIndex;
        if (selected == settings.Theme) return;
        try
        {
            Persist(settings with { Theme = selected });
            AppPalette.Apply(selected);
            SpeedGauge.InvalidateVisual();
            SpeedHistory.InvalidateVisual();
            CurvePlot.InvalidateVisual();
            UpdateLighting();
            FanStatus.Foreground = (System.Windows.Media.Brush)FindResource(
                lastDecision.State == FanState.SensorFallback ? "Warning" : "Muted");
            ThemeStatus.Text = "";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ThemePicker.SelectedIndex = (int)settings.Theme;
            ThemeStatus.Text = "Couldn’t save the theme. " + error.Message;
        }
    }

    private void ChangeSettingsSection(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized || !ReferenceEquals(e.Source, SettingsSections)) return;
        int section = SettingsSections.SelectedIndex;
        GeneralSettings.Visibility = VisibilityFor(section == 0);
        UsbSettings.Visibility = VisibilityFor(section == 1);
        DiagnosticSettings.Visibility = VisibilityFor(section == 2);
        OverlaySettings.Visibility = VisibilityFor(section == 3);
        AboutSettings.Visibility = VisibilityFor(section == 4);
        SettingsScroll.ScrollToTop();
    }

    private void SaveTrayPreference(object sender, RoutedEventArgs e)
    {
        try { Persist(settings with { CloseToTray = CloseToTray.IsChecked == true }); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            CloseToTray.IsChecked = settings.CloseToTray;
            ShowMessage("Couldn’t save the tray preference. " + error.Message);
        }
    }

    private void SaveAdministratorLaunch(object sender, RoutedEventArgs e)
    {
        try
        {
            Persist(settings with { RequestAdministratorOnLaunch = AdministratorLaunch.IsChecked == true });
            AdministratorNotice.Text = "";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            AdministratorLaunch.IsChecked = settings.RequestAdministratorOnLaunch;
            AdministratorNotice.Text = "Couldn’t save the launch preference. " + error.Message;
        }
    }

    private void OpenReleaseDocument(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string file }) return;
        string path = Path.Combine(AppContext.BaseDirectory, file);
        if (!File.Exists(path))
        {
            AboutStatus.Text = "This document is included with the portable ZIP and MSI.";
            return;
        }
        try
        {
            using var editor = Process.Start(new ProcessStartInfo("notepad.exe") { ArgumentList = { path } });
            AboutStatus.Text = "";
        }
        catch (Win32Exception error) { AboutStatus.Text = "Couldn’t open the document. " + error.Message; }
    }

    private async void CheckForUpdates(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        OpenUpdateButton.Visibility = Visibility.Collapsed;
        availableUpdate = null;
        UpdateStatus.Text = "Checking GitHub…";
        try
        {
            Version latest = await ReleaseUpdates.LatestAsync(lifetime.Token);
            if (closing) return;
            int comparison = latest.CompareTo(Version.Parse(ReleaseVersion));
            UpdateStatus.Text = comparison switch
            {
                > 0 => $"Version {latest} is available.",
                0 => "You have the latest release.",
                _ => $"The latest public release is {latest}. You’re running {ReleaseVersion}."
            };
            if (comparison > 0)
            {
                availableUpdate = latest;
                OpenUpdateButton.Visibility = Visibility.Visible;
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or IOException or JsonException or InvalidDataException)
        {
            Debug.WriteLine(error);
            if (!closing) UpdateStatus.Text = "Couldn’t check GitHub for updates. Try again later.";
        }
        finally
        {
            if (!closing) CheckUpdatesButton.IsEnabled = true;
        }
    }

    private void OpenUpdate(object sender, RoutedEventArgs e)
    {
        if (availableUpdate is not { } version) return;
        try
        {
            using var browser = Process.Start(new ProcessStartInfo(ReleaseUpdates.ReleasePage(version)) { UseShellExecute = true });
        }
        catch (Win32Exception error)
        {
            Debug.WriteLine(error);
            UpdateStatus.Text = "Couldn’t open the GitHub release in your browser.";
        }
    }

    private void UpdateRecoveryHelp()
    {
        UsbHelp.Text = simulate ? "Turn off Simulation in Diagnostics to send commands to the cooler." :
            coolerStatus is { Powered: false } ? "Turn on the cooler here or hold its physical power button." :
            cooler is null ? "Connect one V12 Ultra using its USB data cable and plug in its power adapter." :
            coolerStatus is null ? "The HID interface is visible. Check the power adapter and reconnect the USB data cable if it still doesn’t respond." : "";

        string action = simulate ? "" : coolerStatus switch
        {
            { Powered: false } => "Turn cooler on",
            null => "Check USB connection",
            _ when CpuSource.Text.Length == 0 && GpuSource.Text.Length == 0 => "Get temperature readings",
            _ => ""
        };
        CoolingHelp.Visibility = VisibilityFor(action.Length > 0);
        CoolingHelp.Content = action;
    }

    private void OpenCoolingHelp(object sender, RoutedEventArgs e)
    {
        Navigation.SelectedItem = SettingsPage;
        SettingsSections.SelectedIndex = coolerStatus is { Powered: true } ? 2 : 1;
    }
}
