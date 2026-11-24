using System;
using System.IO;
using System.Linq;

namespace Llano.Core;

public static class AppDataFiles
{
    private const string SettingsName = "settings.json";
    private const string SettingsBackupPrefix = "settings-recovery-";
    public const string HistoryName = "cooling-history.bin";

    public static string DirectoryPath
    {
        get
        {
            string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(localData)) throw new DirectoryNotFoundException("The Windows local app data folder is unavailable.");
            return Path.Combine(localData, "LlanoUltraControl");
        }
    }

    public static string SettingsPath => Path.Combine(DirectoryPath, SettingsName);

    internal static string SettingsBackupName(Guid id) => $"{SettingsBackupPrefix}{id:N}.json";

    public static void RemoveSettings(string directoryPath)
    {
        var directory = new DirectoryInfo(directoryPath);
        if (!directory.Exists) return;
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The Llano Ultra Control data folder points to another location.");

        File.Delete(Path.Combine(directory.FullName, SettingsName));
        File.Delete(Path.Combine(directory.FullName, HistoryName));
        foreach (var file in directory.EnumerateFiles())
        {
            string name = Path.GetFileNameWithoutExtension(file.Name);
            bool temporary = file.Extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase) && Guid.TryParseExact(name, "N", out _);
            bool recovery = file.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase) &&
                name.StartsWith(SettingsBackupPrefix, StringComparison.OrdinalIgnoreCase) &&
                Guid.TryParseExact(name.Substring(SettingsBackupPrefix.Length), "N", out _);
            if (temporary || recovery) file.Delete();
        }

        if (!directory.EnumerateFileSystemInfos().Any()) directory.Delete();
    }
}
