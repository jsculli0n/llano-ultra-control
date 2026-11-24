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
    private const long MaximumBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private readonly string fullPath = Path.GetFullPath(path);
    private bool preserveOriginal;

    public AppSettings Load()
    {
        try
        {
            using var stream = File.OpenRead(fullPath);
            CheckSize(stream.Length);
            var settings = JsonSerializer.Deserialize<AppSettings>(stream, Json) ?? throw new InvalidDataException("Settings are empty.");
            Validate(settings);
            preserveOriginal = false;
            return settings;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            preserveOriginal = false;
            return new();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            preserveOriginal = true;
            throw;
        }
    }

    public void Save(AppSettings settings)
    {
        Validate(settings);
        string directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var writeId = Guid.NewGuid();
        string temporary = Path.Combine(directory, $"{writeId:N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, settings, Json);
                CheckSize(stream.Length);
                stream.Flush(flushToDisk: true);
            }
            if (preserveOriginal)
            {
                string backup = Path.Combine(directory, AppDataFiles.SettingsBackupName(writeId));
                try { File.Replace(temporary, fullPath, backup); }
                catch (FileNotFoundException) { File.Move(temporary, fullPath); }
                preserveOriginal = false;
            }
            else File.Move(temporary, fullPath, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    private static void CheckSize(long length)
    {
        if (length > MaximumBytes) throw new IOException("Settings exceed the 4 MiB size limit.");
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
