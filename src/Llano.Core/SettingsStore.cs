using System.Text.Json;
using System.Text.Json.Serialization;

namespace Llano.Core;

public enum AppTheme { EmberRed, Violet, IceBlue, Graphite }

public sealed record AppSettings
{
    public int Version { get; init; } = 1;
    public FanProfile[] Profiles { get; init; } = [new()];
    public string SelectedProfile { get; init; } = "Balanced";
    public bool ResumeAtSignIn { get; init; }
    public bool PowerOffAtShutdown { get; init; }
    public AppTheme Theme { get; init; } = AppTheme.EmberRed;
    public bool CloseToTray { get; init; } = true;
    public CoolingMode? LastCoolingMode { get; init; }
    public int ManualRpm { get; init; } = 1200;
    public Lighting? LastLighting { get; init; }
    public LightingPreset[] LightingPresets { get; init; } = [];
    public StatsOverlaySettings StatsOverlay { get; init; } = new();
}

public sealed record LightingPreset(string Name, Lighting Lighting);

public sealed class SettingsStore(string path, SpeedLimits limits)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public AppSettings Load()
    {
        if (!File.Exists(path)) return new();
        using var stream = File.OpenRead(path);
        var settings = JsonSerializer.Deserialize<AppSettings>(stream, Json) ?? throw new InvalidDataException("Settings are empty.");
        Validate(settings);
        return settings;
    }

    public void Save(AppSettings settings)
    {
        Validate(settings);
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $"{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, settings, Json);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    private void Validate(AppSettings settings)
    {
        if (settings.Version != 1 || settings.Profiles is not { Length: > 0 } ||
            !Enum.IsDefined(settings.Theme) || settings.StatsOverlay?.IsValid() != true ||
            settings.Profiles.Any(p => p is null || p.Mode == FanMode.Automatic || p.Validate(limits, requireSensors: false) is not null) ||
            settings.Profiles.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != settings.Profiles.Length ||
            !settings.Profiles.Any(p => p.Name == settings.SelectedProfile) ||
            (settings.LastCoolingMode is { } mode && !Enum.IsDefined(mode)) ||
            settings.ManualRpm < limits.Minimum || settings.ManualRpm > limits.Maximum ||
            settings.LastLighting?.IsValid() == false ||
            settings.LightingPresets is null ||
            settings.LightingPresets.Any(p => p is null || string.IsNullOrWhiteSpace(p.Name) ||
                p.Name.Length > 32 || p.Lighting?.IsValid() != true) ||
            settings.LightingPresets.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != settings.LightingPresets.Length)
            throw new InvalidDataException("The settings version or profile data is invalid.");
    }
}
