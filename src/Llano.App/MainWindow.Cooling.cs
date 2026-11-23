using System.Windows;
using System.Windows.Controls;
using Llano.Core;

namespace Llano.App;

public partial class MainWindow
{
    private CoolingMode coolingMode = CoolingMode.Balanced;

    private void LoadCoolingChoice()
    {
        ManualSpeed.Value = settings.ManualRpm;
        var choice = settings.LastCoolingMode switch
        {
            CoolingMode.Quiet => QuietChoice,
            CoolingMode.Boost => BoostChoice,
            CoolingMode.Manual => ManualChoice,
            _ => BalancedChoice
        };
        choice.IsChecked = true;
        ChangeManualSpeed(this, null!);
    }

    private void ChooseCoolingMode(object sender, RoutedEventArgs e)
    {
        if (!initialized || sender is not RadioButton { Tag: string tag }) return;
        coolingMode = Enum.Parse<CoolingMode>(tag);
        ManualControls.Visibility = VisibilityFor(coolingMode == CoolingMode.Manual);
        CoolingModeHint.Text = coolingMode switch
        {
            CoolingMode.Quiet => "Starts at 300 RPM at light loads. Ramps up as heat builds.",
            CoolingMode.Balanced => "Responds to sustained CPU or GPU heat.",
            CoolingMode.Boost => "Keeps more airflow available under sustained load.",
            _ => "Holds the selected RPM."
        };
        UpdateControlAvailability();
    }

    private void ChangeManualSpeed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!initialized) return;
        ManualSpeedLabel.Text = $"{ManualSpeed.Value:N0} RPM";
        UpdateCoolingActions();
    }

    private FanProfile CoolingProfile() => CoolingPresets.Create(coolingMode, (int)ManualSpeed.Value);

    private bool CanControlCooler => !simulate && coolerStatus is { Powered: true };
    private bool IsSameControlSession => controller.Enabled && (driveHardware || !CanControlCooler);
    private bool IsCoolingChoiceApplied => IsSameControlSession && activeCoolingMode == coolingMode &&
        (coolingMode != CoolingMode.Manual || runningProfile?.FixedRpm == (int)ManualSpeed.Value);

    private void UpdateCoolingActions()
    {
        bool applied = IsCoolingChoiceApplied;
        string choice = coolingMode == CoolingMode.Manual ? "manual speed" : coolingMode.ToString();
        StartControl.Content = applied
            ? driveHardware ? $"{coolingMode} applied" : $"{coolingMode} previewing"
            : CanControlCooler ? $"_Apply {choice}" : $"_Preview {choice}";
        StartControl.IsEnabled = !powerChanging && !applied;
        AppliedModeLabel.Text = controller.Enabled && runningProfile is { } profile
            ? driveHardware ? $"In use: {profile.Name}" : $"Preview: {profile.Name}"
            : "";

        bool customApplied = Navigation.SelectedItem == FanPage && IsCustomDraftApplied();
        CustomStart.Content = customApplied ? driveHardware ? "Profile applied" : "Preview active" :
            CanControlCooler ? "_Apply profile" : "_Preview profile";
        CustomStart.IsEnabled = !powerChanging && !customApplied;
    }

    private void StartFan(object sender, RoutedEventArgs e)
    {
        bool custom = ReferenceEquals(sender, CustomStart);
        if (powerChanging || (custom ? IsCustomDraftApplied() : IsCoolingChoiceApplied)) return;
        try
        {
            var next = custom ? ReadProfile(forPreview: true) : CoolingProfile();
            bool hardware = CanControlCooler;
            runningProfile = next;
            activeCoolingMode = custom ? null : coolingMode;
            driveHardware = hardware;
            ResumeControl();
            string selectedProfile = ProfilesList.SelectedItem as string ?? settings.SelectedProfile;
            int manualRpm = (int)ManualSpeed.Value;
            if (driveHardware && (settings.LastCoolingMode != activeCoolingMode || settings.ManualRpm != manualRpm ||
                settings.SelectedProfile != selectedProfile))
                Persist(settings with
                {
                    LastCoolingMode = activeCoolingMode,
                    ManualRpm = manualRpm,
                    SelectedProfile = selectedProfile
                });
        }
        catch (ArgumentException error) { ShowMessage(error.Message); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ShowMessage($"Cooling is active. Couldn’t save the sign-in choice. {error.Message}");
        }
    }

    private IReadOnlyDictionary<string, Temperature> CurrentTemperatures(SensorReading? cpu, SensorReading? gpu)
    {
        if (!controller.Enabled || runningProfile is not { Mode: not FanMode.Fixed } profile) return NoTemperatures;
        if (profile.Mode != FanMode.Automatic)
            return temperatureSensors.Where(r => IsFresh(r) && r.Value.HasValue)
                .ToDictionary(r => r.Id, r => new Temperature(r.Value!.Value, r.SampledAt));

        var inputs = new Dictionary<string, Temperature>(2);
        if (cpu is { Value: double cpuValue }) inputs[CoolingPresets.CpuInput] = new(cpuValue, cpu.SampledAt) { Source = cpu.Id };
        if (gpu is { Value: double gpuValue }) inputs[CoolingPresets.GpuInput] = new(gpuValue, gpu.SampledAt) { Source = gpu.Id };
        return inputs;
    }

    private void UpdateCoolingDashboard(TimeSpan now, FanDecision decision)
    {
        int? actual = simulate ? decision.TargetRpm : coolerStatus is { Powered: true } status ? status.SettingRpm : null;
        SpeedLabel.Text = simulate ? "Simulated speed" : "Cooler speed setting";
        SpeedTile.Text = actual is { } rpm ? $"{rpm:N0} RPM" : !simulate && coolerStatus is { Powered: false } ? "Off" : "—";
        TargetTile.Text = !simulate && decision.TargetRpm is { } target && actual != target ? $"Target {target:N0} RPM" : "";
        CoolingMotionLabel.Text = decision.Motion switch
        {
            FanMotion.RampingUp => "Increasing speed",
            FanMotion.Holding => "Holding speed",
            FanMotion.RampingDown => "Lowering speed",
            FanMotion.HighTemperature => "High-temperature response",
            FanMotion.Steady => $"{runningProfile?.Name} cooling active",
            _ => decision.State switch
            {
                FanState.SensorFallback => "Temperature input lost",
                FanState.WaitingForCooler => "Waiting for cooler",
                FanState.WaitingForSensors => "Waiting for temperature",
                FanState.DialOverride => "Physical dial control",
                FanState.Fixed => "Fixed speed active",
                FanState.Smart => "Custom curve active",
                _ => "Cooling paused"
            }
        };
        CoolingMotionLabel.Foreground = (System.Windows.Media.Brush)FindResource(
            decision.State == FanState.SensorFallback || decision.Motion == FanMotion.HighTemperature ? "Warning" : "Accent");
        SpeedGauge.ShowSpeed(actual);
        SpeedHistory.Record(now, actual);
    }
}
