using System.Diagnostics;
using System.Security.Principal;

namespace Llano.App;

internal enum StartupMode { Off, Standard, Elevated }

internal static class StartupRegistration
{
    private const string TaskPrefix = "LlanoUltraControl-";
    private const int TaskNotFound = unchecked((int)0x80070002);
    private static string Sid => WindowsIdentity.GetCurrent().User!.Value;

    public static string InstalledPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Llano Ultra Control", "LlanoUltraControl.exe");

    public static bool IsInstalled => string.Equals(Environment.ProcessPath, InstalledPath, StringComparison.OrdinalIgnoreCase);
    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public static StartupMode GetMode()
    {
        dynamic root = Root();
        try
        {
            dynamic task = root.GetTask(TaskPrefix + Sid);
            return (bool)task.Enabled ? ((int)task.Definition.Principal.RunLevel == 1 ? StartupMode.Elevated : StartupMode.Standard)
                : StartupMode.Off;
        }
        catch (Exception error) when (error.HResult == TaskNotFound) { return StartupMode.Off; }
    }

    public static async Task SetAsync(StartupMode mode)
    {
        if (!IsInstalled) throw new InvalidOperationException("Install the MSI to configure Windows startup.");
        if (IsElevated || (mode != StartupMode.Elevated && GetMode() != StartupMode.Elevated))
        {
            Apply(mode);
            return;
        }

        using var process = Process.Start(new ProcessStartInfo(InstalledPath, $"--configure-startup {Sid}:{mode}")
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(InstalledPath)!
        }) ?? throw new InvalidOperationException("Windows could not start the administrator setup.");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new InvalidOperationException("Startup setup needs administrator approval from this Windows account.");
    }

    public static bool ApplyElevatedCommand(string command)
    {
        string[] parts = command.Split(':');
        if (!IsInstalled || !IsElevated || parts.Length != 2 || parts[0] != Sid ||
            !Enum.TryParse<StartupMode>(parts[1], out var mode) || !Enum.IsDefined(mode)) return false;
        try { Apply(mode); return true; }
        catch (Exception error) { Debug.WriteLine(error); return false; }
    }

    public static bool RemoveInstalledTasks()
    {
        if (!IsInstalled || !IsElevated) return false;
        try
        {
            dynamic root = Root();
            bool removedAll = true;
            foreach (dynamic task in root.GetTasks(1))
            {
                string name = task.Name;
                if (!name.StartsWith(TaskPrefix + "S-1-", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    dynamic actions = task.Definition.Actions;
                    if (actions.Count == 1 && actions[1].Type == 0 &&
                        string.Equals((string)actions[1].Path, InstalledPath, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals((string)actions[1].Arguments, "--tray", StringComparison.Ordinal))
                        root.DeleteTask(name, 0);
                }
                catch (Exception error) when (error.HResult == TaskNotFound) { }
                catch (Exception error)
                {
                    Debug.WriteLine(error);
                    removedAll = false;
                }
            }
            return removedAll;
        }
        catch (Exception error) { Debug.WriteLine(error); return false; }
    }

    private static void Apply(StartupMode mode)
    {
        dynamic root = Root();
        string name = TaskPrefix + Sid;
        if (mode == StartupMode.Off)
        {
            try { root.DeleteTask(name, 0); }
            catch (Exception error) when (error.HResult == TaskNotFound) { }
            return;
        }

        dynamic service = Connect();
        dynamic task = service.NewTask(0);
        task.RegistrationInfo.Description = "Launches Llano Ultra Control in the notification area at sign-in.";
        task.Principal.UserId = Sid;
        task.Principal.LogonType = 3;
        task.Principal.RunLevel = mode == StartupMode.Elevated ? 1 : 0;
        task.Settings.DisallowStartIfOnBatteries = false;
        task.Settings.StopIfGoingOnBatteries = false;
        task.Settings.ExecutionTimeLimit = "PT0S";
        task.Settings.MultipleInstances = 2;
        dynamic trigger = task.Triggers.Create(9);
        trigger.UserId = Sid;
        dynamic action = task.Actions.Create(0);
        action.Path = InstalledPath;
        action.WorkingDirectory = Path.GetDirectoryName(InstalledPath);
        action.Arguments = "--tray";
        root.RegisterTaskDefinition(name, task, 6, Sid, null, 3, null);
    }

    private static dynamic Root() => Connect().GetFolder("\\");

    private static dynamic Connect()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!;
        dynamic service = Activator.CreateInstance(type)!;
        service.Connect();
        return service;
    }
}
