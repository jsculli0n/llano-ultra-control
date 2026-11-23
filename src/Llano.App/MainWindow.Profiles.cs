using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Llano.Core;

namespace Llano.App;

public partial class MainWindow
{
    private void ReloadProfiles()
    {
        ProfilesList.ItemsSource = settings.Profiles.Select(p => p.Name).ToArray();
        ProfilesList.SelectedItem = settings.SelectedProfile;
        RemoveProfile.IsEnabled = settings.Profiles.Length > 1;
    }

    private void SelectProfile(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized || ProfilesList.SelectedItem is not string name) return;
        var profile = settings.Profiles.First(p => p.Name == name);
        loading = true;
        try
        {
            ProfileName.Text = profile.Name;
            FanModeList.SelectedIndex = (int)profile.Mode;
            MinimumRpm.Text = profile.MinimumRpm.ToString(CultureInfo.InvariantCulture);
            MaximumRpm.Text = profile.MaximumRpm.ToString(CultureInfo.InvariantCulture);
            FixedRpm.Text = profile.FixedRpm.ToString(CultureInfo.InvariantCulture);
            SharedCurve.Load(profile.SharedCurve);
            CpuCurve.Load(profile.CpuCurve);
            GpuCurve.Load(profile.GpuCurve);
            SharedSensors.UnselectAll();
            CpuSensor.SelectedIndex = GpuSensor.SelectedIndex = -1;
            string[] shared = simulate ? [DemoCpuId, DemoGpuId] : profile.SharedSensors;
            var cpu = simulate ? DemoCpuId : profile.CpuSensor;
            var gpu = simulate ? DemoGpuId : profile.GpuSensor;
            UpdateSensorSelectors(shared, cpu, gpu, force: true);
        }
        finally { loading = false; }
        ChangeFanMode(this, null!);
        UpdateLighting();
    }

    private void ChangeFanMode(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized || loading) return;
        bool fixedMode = FanModeList.SelectedIndex == (int)FanMode.Fixed;
        bool separate = FanModeList.SelectedIndex == (int)FanMode.SeparateCurves;
        FixedEditor.Visibility = FixedHint.Visibility = VisibilityFor(fixedMode);
        GraphHost.Visibility = CurveEditors.Visibility = VisibilityFor(!fixedMode);
        SeparateEditor.Visibility = SeparateCurveEditors.Visibility = VisibilityFor(separate);
        SharedEditor.Visibility = SharedCurve.Visibility = VisibilityFor(!fixedMode && !separate);
        CurveTitle.Text = fixedMode ? "Fixed speed" : "Fan curve";
        ChangeCurve(this, EventArgs.Empty);
    }

    private void ChangeCurve(object? sender, EventArgs e)
    {
        if (!initialized || loading) return;
        bool separate = FanModeList.SelectedIndex == (int)FanMode.SeparateCurves;
        CurvePlot.Plot((separate ? CpuCurve.Values : SharedCurve.Values) ?? [], separate ? GpuCurve.Values : null);
        UpdateCurveEstimate();
        UpdateCoolingActions();
    }

    private void ChangeCurveEditor(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized) return;
        CpuCurve.Visibility = VisibilityFor(CurveSensor.SelectedIndex == 0);
        GpuCurve.Visibility = VisibilityFor(CurveSensor.SelectedIndex == 1);
        UpdateCurveEstimate();
    }

    private void ChangePreviewTemperature(object sender, RoutedEventArgs e)
    {
        if (!initialized || loading) return;
        UpdateCurveEstimate();
        if (sender != CurvePreviewTemperature) UpdateCoolingActions();
    }

    private void ChangeProfileDraft(object sender, RoutedEventArgs e)
    {
        if (initialized && !loading && Navigation.SelectedItem == FanPage) UpdateCoolingActions();
    }

    private bool IsCustomDraftApplied()
    {
        if (!IsSameControlSession || activeCoolingMode is not null || runningProfile is not { } active) return false;
        try
        {
            var draft = ReadProfile(forPreview: true);
            if (active.Name != draft.Name || active.Mode != draft.Mode ||
                active.MinimumRpm != draft.MinimumRpm || active.MaximumRpm != draft.MaximumRpm) return false;
            return active.Mode switch
            {
                FanMode.Fixed => active.FixedRpm == draft.FixedRpm,
                FanMode.SharedCurve => active.SharedCurve.SequenceEqual(draft.SharedCurve) &&
                    active.SharedSensors.ToHashSet(StringComparer.Ordinal).SetEquals(draft.SharedSensors),
                FanMode.SeparateCurves => active.CpuSensor == draft.CpuSensor && active.GpuSensor == draft.GpuSensor &&
                    active.CpuCurve.SequenceEqual(draft.CpuCurve) && active.GpuCurve.SequenceEqual(draft.GpuCurve),
                _ => false
            };
        }
        catch (ArgumentException) { return false; }
    }

    private void UpdateCurveEstimate()
    {
        var mode = (FanMode)FanModeList.SelectedIndex;
        bool gpu = mode == FanMode.SeparateCurves && CurveSensor.SelectedIndex == 1;
        var points = mode == FanMode.SeparateCurves ? (gpu ? GpuCurve.Values : CpuCurve.Values) : SharedCurve.Values;
        if (mode == FanMode.Fixed || points is not { Length: >= 2 } ||
            !int.TryParse(MinimumRpm.Text, out int minimum) || !int.TryParse(MaximumRpm.Text, out int maximum) ||
            !CoolerLimits.ContainsStep(minimum, maximum))
        {
            CurveEstimate.Text = "—";
            CurvePlot.SetPreview(null, gpu);
            return;
        }

        double celsius = CurvePreviewTemperature.Value;
        double demand = FanController.Interpolate(points, celsius);
        int rpm = CoolerLimits.Quantize(demand, minimum, maximum);
        CurveEstimate.Text = $"At {celsius:0}°C  →  {rpm:N0} RPM";
        CurvePlot.SetPreview(new(celsius, rpm), gpu);
    }

    private void LoadCurvePreset(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !Enum.TryParse<CoolingMode>(tag, out var mode)) return;
        var template = CoolingPresets.Create(mode, 0);
        loading = true;
        try
        {
            MinimumRpm.Text = template.MinimumRpm.ToString(CultureInfo.InvariantCulture);
            MaximumRpm.Text = template.MaximumRpm.ToString(CultureInfo.InvariantCulture);
            SharedCurve.Load(template.CpuCurve);
            CpuCurve.Load(template.CpuCurve);
            GpuCurve.Load(template.GpuCurve);
            if (FanModeList.SelectedIndex == (int)FanMode.Fixed) FanModeList.SelectedIndex = (int)FanMode.SharedCurve;
        }
        finally { loading = false; }
        ChangeFanMode(this, null!);
        ShowMessage($"Loaded {mode} into the draft. Save or apply when ready.");
    }

    private static Visibility VisibilityFor(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private FanProfile ReadProfile(bool forPreview)
    {
        var saved = settings.Profiles.FirstOrDefault(p => p.Name == ProfilesList.SelectedItem as string)
            ?? throw new ArgumentException("Select a saved profile.");
        var mode = (FanMode)FanModeList.SelectedIndex;
        if (!int.TryParse(MinimumRpm.Text, out var low) || !int.TryParse(MaximumRpm.Text, out var high))
            throw new ArgumentException("Enter whole numbers for minimum and maximum RPM.");
        if (!int.TryParse(FixedRpm.Text, out var fixedSpeed))
        {
            if (mode == FanMode.Fixed) throw new ArgumentException("Enter a whole number for fixed RPM.");
            fixedSpeed = saved.FixedRpm;
        }
        var profile = new FanProfile
        {
            Name = ProfileName.Text.Trim(),
            Mode = mode,
            MinimumRpm = low,
            MaximumRpm = high,
            FixedRpm = fixedSpeed,
            SharedSensors = simulate && !forPreview ? saved.SharedSensors :
                SharedSensors.SelectedItems.Cast<SensorReading>().Select(r => r.Id).ToArray(),
            CpuSensor = simulate && !forPreview ? saved.CpuSensor : CpuSensor.SelectedValue as string,
            GpuSensor = simulate && !forPreview ? saved.GpuSensor : GpuSensor.SelectedValue as string,
            SharedCurve = ReadCurve(SharedCurve, saved.SharedCurve, mode == FanMode.SharedCurve, "shared"),
            CpuCurve = ReadCurve(CpuCurve, saved.CpuCurve, mode == FanMode.SeparateCurves, "CPU"),
            GpuCurve = ReadCurve(GpuCurve, saved.GpuCurve, mode == FanMode.SeparateCurves, "GPU"),
            Lighting = saved.Lighting
        };
        if (profile.Validate(CoolerLimits, requireSensors: forPreview) is { } error) throw new ArgumentException(error);
        return profile;
    }

    private static CurvePoint[] ReadCurve(CurveEditor editor, CurvePoint[] saved, bool active, string label) =>
        editor.Values ?? (active ? throw new ArgumentException($"Correct the points on the {label} curve.") : saved);

    private void SaveProfile(object sender, RoutedEventArgs e) => SaveProfile(create: false);
    private void SaveNewProfile(object sender, RoutedEventArgs e) => SaveProfile(create: true);

    private void SaveProfile(bool create)
    {
        try
        {
            var edited = ReadProfile(forPreview: false);
            string original = (string)ProfilesList.SelectedItem;
            if (settings.Profiles.Any(p => (create || p.Name != original) &&
                p.Name.Equals(edited.Name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Use a different profile name.");
            FanProfile[] profiles = create ? [.. settings.Profiles, edited] :
                settings.Profiles.Select(p => p.Name == original ? edited : p).ToArray();
            Persist(settings with { Profiles = profiles, SelectedProfile = edited.Name });
            ReloadProfiles();
            ShowMessage($"Saved profile {edited.Name}.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ShowMessage(error.Message);
        }
    }

    private void DeleteProfile(object sender, RoutedEventArgs e)
    {
        if (settings.Profiles.Length <= 1 || ProfilesList.SelectedItem is not string name) return;
        try
        {
            var profiles = settings.Profiles.Where(p => p.Name != name).ToArray();
            Persist(settings with { Profiles = profiles, SelectedProfile = profiles[0].Name });
            ReloadProfiles();
            ShowMessage($"Removed profile {name}.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { ShowMessage(error.Message); }
    }

    private void Persist(AppSettings next)
    {
        store.Save(next);
        settings = next;
    }
}
