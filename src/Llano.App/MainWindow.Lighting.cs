using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Llano.Core;

namespace Llano.App;

public partial class MainWindow
{
    private bool lightingInFlight;
    private static readonly LightingPreset[] BuiltInLights =
    [
        new("Off", new(Enabled: false, Color: V12UltraStatus.Palette[0], Brightness: 25)),
        Solid("Crimson", 0, 70),
        Solid("Low red", 0, 18),
        Solid("Ice blue", 1, 60),
        Solid("Green", 2, 55),
        Solid("Violet", 3, 55),
        Solid("Amber", 4, 50),
        new("Blue pulse", new(true, LightingEffect.Breathing, V12UltraStatus.Palette[1], 50, 1)),
        new("Violet pulse", new(true, LightingEffect.Breathing, V12UltraStatus.Palette[3], 40, 2)),
        new("Rainbow fade", new(true, LightingEffect.ColorGradient, V12UltraStatus.Palette[0], 55, 1)),
        new("Rainbow chase", new(true, LightingEffect.ColorChase, V12UltraStatus.Palette[0], 70, 3))
    ];

    private static LightingPreset Solid(string name, int color, int brightness) =>
        new(name, new(Color: V12UltraStatus.Palette[color], Brightness: brightness));

    private void InitializeLighting()
    {
        loading = true;
        string[] names = ["Red", "Blue", "Green", "Violet", "Amber"];
        LightColor.ItemsSource = V12UltraStatus.Palette.Select((hex, index) => new SwatchOption(names[index], SolidBrush(hex, 1))).ToArray();
        ReloadLightingPresets();
        loading = false;
        var previous = settings.LastLighting ?? settings.Profiles.First(p => p.Name == settings.SelectedProfile).Lighting;
        LoadLighting(previous);
    }

    private void ReloadLightingPresets(string? selectedName = null)
    {
        var options = BuiltInLights.Select(preset => new LightingOption(preset, false))
            .Concat(settings.LightingPresets.Select(preset => new LightingOption(preset, true))).ToArray();
        LightingLooks.ItemsSource = options;
        if (selectedName is not null) LightingLooks.SelectedItem = options.FirstOrDefault(o => o.Name == selectedName);
    }

    private void ChooseLightingPreset(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized || loading || LightingLooks.SelectedItem is not LightingOption option) return;
        LoadLighting(option.Preset.Lighting);
        LightingPresetName.Text = option.Saved ? option.Name : "";
        RemoveLightingButton.Visibility = VisibilityFor(option.Saved);
    }

    private void LoadLighting(Lighting lighting)
    {
        loading = true;
        LightingEnabled.IsChecked = lighting.Enabled;
        LightEffect.SelectedIndex = (int)lighting.Effect;
        LightColor.SelectedIndex = V12UltraStatus.PaletteIndex(lighting.Color);
        Brightness.Value = lighting.Brightness;
        LightSpeed.SelectedIndex = lighting.Speed is { } speed ? speed + 1 : 0;
        loading = false;
        UpdateLighting();
    }

    private void ChangeLighting(object sender, RoutedEventArgs e)
    {
        if (!initialized || loading) return;
        LightingLooks.SelectedItem = null;
        RemoveLightingButton.Visibility = Visibility.Collapsed;
        UpdateLighting();
    }

    private Lighting ReadLighting() => new(LightingEnabled.IsChecked == true, (LightingEffect)LightEffect.SelectedIndex,
        V12UltraStatus.Palette[LightColor.SelectedIndex], (int)Brightness.Value,
        LightSpeed.SelectedIndex > 0 ? LightSpeed.SelectedIndex - 1 : null);

    private void LoadDeviceLighting(object sender, RoutedEventArgs e)
    {
        if (simulate || coolerStatus is not { Powered: true } live) return;
        LightingLooks.SelectedItem = null;
        LightingPresetName.Clear();
        RemoveLightingButton.Visibility = Visibility.Collapsed;
        LoadLighting(new(live.LightingEnabled, (LightingEffect)live.LightingEffectIndex,
            V12UltraStatus.Palette[live.LightingColorIndex], live.BrightnessPercent, live.LightingSpeed));
        LightingLookTitle.Text = "Current cooler lighting";
    }

    private void ApplyRgb(object sender, RoutedEventArgs e)
    {
        if (!ApplyLighting.IsEnabled) return;
        pendingLighting = ReadLighting();
        ShowMessage("Applying lighting…");
        Refresh();
    }

    private void UpdateLightingActions()
    {
        bool reachable = !powerChanging && CanControlCooler;
        bool busy = lightingInFlight || pendingLighting is not null;
        bool valid = LightEffect.SelectedIndex >= 0 && LightColor.SelectedIndex >= 0;
        bool same = reachable && valid && coolerStatus is { } status && LightingMatches(ReadLighting(), status);
        ApplyLighting.Content = busy ? "Applying…" : same ? "Lighting applied" : "_Apply to cooler";
        ApplyLighting.IsEnabled = reachable && valid && !busy && !same;
        ReadLightingButton.IsEnabled = reachable && !busy;
    }

    private static bool LightingMatches(Lighting draft, V12UltraStatus status) =>
        draft.Enabled == status.LightingEnabled && (int)draft.Effect == status.LightingEffectIndex &&
        (draft.Effect is LightingEffect.ColorGradient or LightingEffect.ColorChase ||
            V12UltraStatus.PaletteIndex(draft.Color) == status.LightingColorIndex) &&
        (draft.Speed is null || draft.Speed == status.LightingSpeed) && draft.Brightness == status.BrightnessPercent;

    private void RememberLighting(Lighting lighting)
    {
        ShowMessage("Lighting applied.");
        try { Persist(settings with { LastLighting = lighting }); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            ShowMessage($"Lighting applied. Couldn’t save the last look. {error.Message}");
        }
    }

    private void SaveLighting(object sender, RoutedEventArgs e)
    {
        string name = LightingPresetName.Text.Trim();
        if (name.Length == 0)
        {
            ShowMessage("Enter a name for this lighting preset.");
            LightingPresetName.Focus();
            return;
        }
        if (BuiltInLights.Any(preset => preset.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            ShowMessage("Choose a different name for your preset.");
            return;
        }
        try
        {
            var existing = settings.LightingPresets.Where(preset => !preset.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            Persist(settings with { LightingPresets = [.. existing, new(name, ReadLighting())] });
            ReloadLightingPresets(name);
            ShowMessage($"Saved lighting preset {name}.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { ShowMessage(error.Message); }
    }

    private void RemoveLightingPreset(object sender, RoutedEventArgs e)
    {
        if (LightingLooks.SelectedItem is not LightingOption { Saved: true } option) return;
        try
        {
            Persist(settings with { LightingPresets = settings.LightingPresets.Where(preset => preset.Name != option.Name).ToArray() });
            ReloadLightingPresets();
            LightingPresetName.Clear();
            RemoveLightingButton.Visibility = Visibility.Collapsed;
            ShowMessage($"Removed lighting preset {option.Name}.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { ShowMessage(error.Message); }
    }

    private void UpdateLighting()
    {
        BrightnessLabel.Text = $"Brightness  ·  {Brightness.Value:0}%";
        UpdateLightingActions();
        var effect = (LightingEffect)LightEffect.SelectedIndex;
        LightingLookTitle.Text = (LightingLooks.SelectedItem as LightingOption)?.Name ?? "Lighting preview";
        bool lit = LightingEnabled.IsChecked == true && Brightness.Value > 0;
        LightingLookDetail.Text = !lit ? "Lights off in preview" : LightColor.SelectedItem is SwatchOption colour &&
            effect is LightingEffect.SolidColor or LightingEffect.Breathing ? $"{colour.Name} · {EffectName(effect)}" : EffectName(effect);
        bool gradient = effect is LightingEffect.ColorGradient or LightingEffect.ColorChase;
        ColorControls.Visibility = VisibilityFor(!gradient);
        LightSpeed.IsEnabled = effect != LightingEffect.SolidColor;
        LightingPreviewEffects.BeginAnimation(OpacityProperty, null);
        LightingPreview.BorderBrush = LightingHalo.BorderBrush = null;
        if (!IsVisible || WindowState == WindowState.Minimized || Navigation.SelectedItem != LightingPage || !lit)
        {
            return;
        }
        double fraction = Math.Pow(Brightness.Value / 100, 0.8);
        TimeSpan duration = TimeSpan.FromSeconds(LightSpeed.SelectedIndex switch { 1 => 3.2, 2 => 2.4, 4 => 0.9, _ => 1.5 });
        bool animate = SystemParameters.ClientAreaAnimation;
        Brush strip = effect switch
        {
            LightingEffect.ColorGradient when animate => FadeSpectrum(fraction, duration * 3),
            LightingEffect.ColorChase when animate => ChaseSpectrum(fraction, duration),
            LightingEffect.ColorGradient or LightingEffect.ColorChase => Spectrum(fraction),
            _ when LightColor.SelectedItem is SwatchOption =>
                SolidBrush(V12UltraStatus.Palette[LightColor.SelectedIndex], fraction),
            _ => Brushes.Transparent
        };
        LightingPreview.BorderBrush = LightingHalo.BorderBrush = strip;
        if (effect == LightingEffect.Breathing && animate) Pulse(duration / 2);
    }

    private static string EffectName(LightingEffect effect) => effect switch
    {
        LightingEffect.Breathing => "Breathing",
        LightingEffect.ColorGradient => "Gradient",
        LightingEffect.ColorChase => "Chase",
        _ => "Solid colour"
    };

    private void Pulse(TimeSpan duration) =>
        LightingPreviewEffects.BeginAnimation(OpacityProperty, new DoubleAnimation(0.16, 1, duration)
        {
            AutoReverse = true,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            RepeatBehavior = RepeatBehavior.Forever
        });

    private static LinearGradientBrush ChaseSpectrum(double brightness, TimeSpan duration)
    {
        var brush = Spectrum(brightness);
        var offset = new TranslateTransform();
        brush.RelativeTransform = offset;
        offset.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(0, 1, duration) { RepeatBehavior = RepeatBehavior.Forever });
        return brush;
    }

    private static LinearGradientBrush FadeSpectrum(double brightness, TimeSpan duration)
    {
        var brush = new LinearGradientBrush { EndPoint = new Point(1, 0.35) };
        var palette = V12UltraStatus.Palette;
        for (int stop = 0; stop < 3; stop++)
        {
            int start = stop * 2;
            var colour = new GradientStop(PreviewColor(palette[start], brightness), stop / 2.0);
            var animation = new ColorAnimationUsingKeyFrames { Duration = duration, RepeatBehavior = RepeatBehavior.Forever };
            for (int frame = 0; frame <= palette.Count; frame++)
            {
                Color next = PreviewColor(palette[(start + frame) % palette.Count], brightness);
                animation.KeyFrames.Add(new LinearColorKeyFrame(next, KeyTime.FromPercent((double)frame / palette.Count)));
            }
            colour.BeginAnimation(GradientStop.ColorProperty, animation);
            brush.GradientStops.Add(colour);
        }
        return brush;
    }

    private static LinearGradientBrush Spectrum(double brightness)
    {
        var brush = new LinearGradientBrush { EndPoint = new Point(1, 0.4), SpreadMethod = GradientSpreadMethod.Repeat };
        var palette = V12UltraStatus.Palette;
        for (int i = 0; i <= palette.Count; i++)
            brush.GradientStops.Add(new(PreviewColor(palette[i % palette.Count], brightness), (double)i / palette.Count));
        return brush;
    }

    private static SolidColorBrush SolidBrush(string hex, double brightness)
    {
        var brush = new SolidColorBrush(PreviewColor(hex, brightness));
        brush.Freeze();
        return brush;
    }

    private static Color PreviewColor(string hex, double brightness)
    {
        uint rgb = uint.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return Color.FromRgb((byte)((rgb >> 16) * brightness), (byte)(((rgb >> 8) & 255) * brightness), (byte)((rgb & 255) * brightness));
    }

    private sealed record SwatchOption(string Name, Brush Swatch);

    private sealed class LightingOption(LightingPreset preset, bool saved)
    {
        public LightingPreset Preset { get; } = preset;
        public string Name => Preset.Name;
        public bool Saved { get; } = saved;
        public string Detail => Preset.Lighting.Enabled ? $"{EffectName(Preset.Lighting.Effect)}  ·  {Preset.Lighting.Brightness}%" : "Lights off";
        public Brush Swatch { get; } = !preset.Lighting.Enabled ? Brushes.Transparent :
            preset.Lighting.Effect is LightingEffect.ColorGradient or LightingEffect.ColorChase ? Spectrum(1) :
            SolidBrush(V12UltraStatus.Palette[V12UltraStatus.PaletteIndex(preset.Lighting.Color)], 1);
    }
}
