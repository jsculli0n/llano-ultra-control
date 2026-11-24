using System.Diagnostics;
using System.Security.Principal;

namespace Llano.App;

internal static class StartupRegistration
{
    private const string TaskPrefix = "LlanoUltraControl-";
    private const string TelemetryPrefix = TaskPrefix + "Telemetry-";
    internal const string LauncherName = "LlanoUltraControl.exe";
    internal const string AppHostName = "LlanoUltraControl.App.exe";
    internal const string TelemetryArgument = "--telemetry-helper";
    private const int TaskNotFound = unchecked((int)0x80070002);
    internal static string Sid
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User!.Value;
        }
    }

    private static readonly string InstallDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Llano Ultra Control");

    public static string InstalledPath { get; } = Path.Combine(InstallDirectory, LauncherName);
    public static string InstalledAppPath { get; } = Path.Combine(InstallDirectory, AppHostName);

    public static bool IsInstalled { get; } =
        string.Equals(Environment.ProcessPath, InstalledAppPath, StringComparison.OrdinalIgnoreCase) && File.Exists(InstalledPath);
    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public static (bool Enabled, bool Elevated) GetStartup()
    {
        dynamic root = Root();
        try
        {
            dynamic task = root.GetTask(TaskPrefix + Sid);
            if (!IsOwnTask(task, "--tray")) throw new InvalidOperationException("An existing Windows task conflicts with Llano startup.");
            return ((bool)task.Enabled, (int)task.Definition.Principal.RunLevel == 1);
        }
        catch (Exception error) when (error.HResult == TaskNotFound) { return default; }
    }

    public static void SetAtSignIn(bool enabled)
    {
        if (!IsInstalled) throw new InvalidOperationException("Install the MSI to configure Windows startup.");
        if (GetStartup().Elevated && !IsElevated)
            throw new InvalidOperationException("Enable sensor and FPS access to update your previous sign-in task.");
        ApplyStartup(enabled);
    }

    public static void TryMigrateElevatedStartup()
    {
        try
        {
            var startup = GetStartup();
            if (!startup.Elevated) return;
            // Keep sensor access for accounts that already approved elevated sign-in.
            if (startup.Enabled && !TelemetryEnabled()) RegisterTelemetry();
            ApplyStartup(startup.Enabled);
        }
        catch (Exception error) { Debug.WriteLine(error); }
    }

    public static bool TelemetryEnabled()
    {
        if (!IsInstalled) return false;
        try
        {
            string sid = Sid;
            dynamic task = Root().GetTask(TelemetryPrefix + sid);
            return (bool)task.Enabled && IsTelemetryTask(task, sid);
        }
        catch (Exception error) when (error.HResult == TaskNotFound) { return false; }
    }

    public static void StartTelemetry()
    {
        if (!IsInstalled) throw new InvalidOperationException("Install the MSI to use the telemetry helper.");
        string sid = Sid;
        dynamic task = Root().GetTask(TelemetryPrefix + sid);
        if (!(bool)task.Enabled || !IsTelemetryTask(task, sid))
            throw new InvalidOperationException("The telemetry task is unavailable.");
        using var process = Process.GetCurrentProcess();
        task.RunEx(null, 4, process.SessionId, null);
    }

    public static async Task SetTelemetryAsync(bool enabled)
    {
        if (!IsInstalled) throw new InvalidOperationException("Install the MSI to set up the telemetry helper.");
        if (IsElevated)
        {
            ApplyTelemetry(enabled);
            return;
        }
        using var process = Process.Start(new ProcessStartInfo(InstalledPath, $"--configure-telemetry {Sid} {(enabled ? "on" : "off")}")
        {
            UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(InstalledPath)!
        }) ?? throw new InvalidOperationException("Windows could not start the helper setup.");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException("Setup needs administrator approval from this Windows account.");
    }

    public static bool ApplyTelemetryCommand(string sid, string value)
    {
        if (!IsInstalled || !IsElevated || sid != Sid || value is not ("on" or "off")) return false;
        try { ApplyTelemetry(value == "on"); return true; }
        catch (Exception error) { Debug.WriteLine(error); return false; }
    }

    private static void ApplyTelemetry(bool enabled)
    {
        var startup = GetStartup();
        if (enabled) RegisterTelemetry();
        if (startup.Elevated) ApplyStartup(startup.Enabled);
        if (!enabled)
        {
            try { Root().DeleteTask(TelemetryPrefix + Sid, 0); }
            catch (Exception error) when (error.HResult == TaskNotFound) { }
        }
    }

    private static void RegisterTelemetry()
    {
        string sid = Sid;
        dynamic task = Connect().NewTask(0);
        task.RegistrationInfo.Description = "Provides CPU sensor and FPS readings on demand to Llano Ultra Control.";
        task.Principal.UserId = sid;
        task.Principal.LogonType = 3;
        task.Principal.RunLevel = 1;
        task.Settings.AllowDemandStart = true;
        task.Settings.DisallowStartIfOnBatteries = false;
        task.Settings.StopIfGoingOnBatteries = false;
        task.Settings.ExecutionTimeLimit = "PT0S";
        task.Settings.MultipleInstances = 2;
        dynamic action = task.Actions.Create(0);
        action.Path = InstalledPath;
        action.WorkingDirectory = Path.GetDirectoryName(InstalledPath);
        action.Arguments = TelemetryArgument;
        // The account can read and run the task. Changing its action requires elevation.
        Root().RegisterTaskDefinition(TelemetryPrefix + sid, task, 6 | 16, sid, null, 3,
            $"O:BAG:BAD:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGX;;;{sid})");
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
                bool telemetry = name.StartsWith(TelemetryPrefix + "S-1-", StringComparison.OrdinalIgnoreCase);
                if (!telemetry && !name.StartsWith(TaskPrefix + "S-1-", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    if (IsOwnTask(task, telemetry ? TelemetryArgument : "--tray"))
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

    private static bool IsOwnTask(dynamic task, string argument)
    {
        dynamic actions = task.Definition.Actions;
        return actions.Count == 1 && actions[1].Type == 0 &&
            string.Equals((string)actions[1].Path, InstalledPath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals((string)actions[1].Arguments, argument, StringComparison.Ordinal);
    }

    private static bool IsTelemetryTask(dynamic task, string sid)
    {
        if (!IsOwnTask(task, TelemetryArgument)) return false;
        dynamic definition = task.Definition;
        dynamic principal = definition.Principal;
        string? user = principal.UserId;
        if ((int)definition.Triggers.Count != 0 || (int)principal.LogonType != 3 || (int)principal.RunLevel != 1 ||
            !string.Equals((string)definition.Actions[1].WorkingDirectory, Path.GetDirectoryName(InstalledPath), StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(user)) return false;
        if (user.Equals(sid, StringComparison.OrdinalIgnoreCase)) return true;
        try { return new NTAccount(user).Translate(typeof(SecurityIdentifier)).Value == sid; }
        catch (IdentityNotMappedException) { return false; }
    }

    private static void ApplyStartup(bool enabled)
    {
        dynamic root = Root();
        string sid = Sid;
        string name = TaskPrefix + sid;
        if (!enabled)
        {
            try { root.DeleteTask(name, 0); }
            catch (Exception error) when (error.HResult == TaskNotFound) { }
            return;
        }

        dynamic service = Connect();
        dynamic task = service.NewTask(0);
        task.RegistrationInfo.Description = "Launches Llano Ultra Control in the notification area at sign-in.";
        task.Principal.UserId = sid;
        task.Principal.LogonType = 3;
        task.Principal.RunLevel = 0;
        task.Settings.DisallowStartIfOnBatteries = false;
        task.Settings.StopIfGoingOnBatteries = false;
        task.Settings.ExecutionTimeLimit = "PT0S";
        task.Settings.MultipleInstances = 2;
        dynamic trigger = task.Triggers.Create(9);
        trigger.UserId = sid;
        dynamic action = task.Actions.Create(0);
        action.Path = InstalledPath;
        action.WorkingDirectory = Path.GetDirectoryName(InstalledPath);
        action.Arguments = "--tray";
        root.RegisterTaskDefinition(name, task, 6 | 16, sid, null, 3,
            $"D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GA;;;{sid})");
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
