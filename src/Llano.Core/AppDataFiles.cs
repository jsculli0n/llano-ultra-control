using System;
using System.IO;
using System.Linq;

namespace Llano.Core;

public static class AppDataFiles
{
    private const string SettingsName = "settings.json";

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

    public static void RemoveSettings(string directoryPath)
    {
        var directory = new DirectoryInfo(directoryPath);
        if (!directory.Exists) return;
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The Llano Ultra Control data folder points to another location.");

        File.Delete(Path.Combine(directory.FullName, SettingsName));
        foreach (var file in directory.EnumerateFiles("*.tmp"))
        {
            if (Guid.TryParseExact(Path.GetFileNameWithoutExtension(file.Name), "N", out _))
                file.Delete();
        }

        if (!directory.EnumerateFileSystemInfos().Any()) directory.Delete();
    }
}
