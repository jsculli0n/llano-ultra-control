#include <windows.h>
#include <shellapi.h>
#include <userenv.h>

static const WCHAR app_name[] = L"LlanoUltraControl.App.exe";
static const WCHAR title[] = L"Llano Ultra Control";

static BOOL is_maintenance_command(PCWSTR argument)
{
    return lstrcmpW(argument, L"--telemetry-helper") == 0 ||
           lstrcmpW(argument, L"--configure-telemetry") == 0 ||
           lstrcmpW(argument, L"--remove-startup-tasks") == 0;
}

static BOOL set_clean_environment(HANDLE token)
{
    WCHAR windows[MAX_PATH], system[MAX_PATH];
    UINT windows_size = GetSystemWindowsDirectoryW(windows, ARRAYSIZE(windows));
    UINT system_size = GetSystemDirectoryW(system, ARRAYSIZE(system));
    if (windows_size < 3 || windows_size >= ARRAYSIZE(windows) || windows[1] != L':' ||
        !system_size || system_size >= ARRAYSIZE(system))
        return FALSE;

    // Replace the entire environment before the managed apphost can read it.
    WCHAR environment[] = L"DOTNET_EnableDiagnostics=0\0";
    WCHAR drive[] = { windows[0], L':', L'\0' };
    if (!SetEnvironmentStringsW(environment) ||
        !SetEnvironmentVariableW(L"PATH", system) ||
        !SetEnvironmentVariableW(L"SystemDrive", drive) ||
        !SetEnvironmentVariableW(L"SystemRoot", windows) ||
        !SetEnvironmentVariableW(L"windir", windows))
        return FALSE;

    // Windows uses the token's profile to resolve the application data known folders.
    WCHAR profile[MAX_PATH];
    DWORD profile_size = ARRAYSIZE(profile);
    if (GetUserProfileDirectoryW(token, profile, &profile_size))
        return SetEnvironmentVariableW(L"USERPROFILE", profile);
    return TRUE;
}

static BOOL prepare_launch(BOOL maintenance, BOOL *protect)
{
    HANDLE token;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token))
        return FALSE;
    TOKEN_ELEVATION information;
    DWORD size;
    BOOL ready = GetTokenInformation(token, TokenElevation, &information, sizeof information, &size);
    if (ready)
    {
        *protect = maintenance || information.TokenIsElevated != 0;
        ready = !*protect || set_clean_environment(token);
    }
    CloseHandle(token);
    return ready;
}

static DWORD start_app(LPWSTR command, PCWSTR app_path, PCWSTR directory, BOOL protect)
{
    static STARTUPINFOEXW startup;
    static JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits;
    HANDLE job = NULL;
    BOOL attributes_initialized = FALSE;
    DWORD flags = 0;
    DWORD exit_code = ERROR_GEN_FAILURE;
    startup.StartupInfo.cb = sizeof startup.StartupInfo;
    if (protect)
    {
        // Bind the helper and its children to the launcher for the lifetime of the task.
        job = CreateJobObjectW(NULL, NULL);
        limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        if (!job || !SetInformationJobObject(job, JobObjectExtendedLimitInformation, &limits, sizeof limits))
            goto cleanup;

        SIZE_T size = 0;
        InitializeProcThreadAttributeList(NULL, 1, 0, &size);
        startup.lpAttributeList = size ? HeapAlloc(GetProcessHeap(), 0, size) : NULL;
        if (!startup.lpAttributeList || !InitializeProcThreadAttributeList(startup.lpAttributeList, 1, 0, &size))
            goto cleanup;
        attributes_initialized = TRUE;
        if (!UpdateProcThreadAttribute(startup.lpAttributeList, 0, PROC_THREAD_ATTRIBUTE_JOB_LIST,
                                       &job, sizeof job, NULL, NULL))
            goto cleanup;
        startup.StartupInfo.cb = sizeof startup;
        flags = EXTENDED_STARTUPINFO_PRESENT;
    }

    PROCESS_INFORMATION child;
    if (!CreateProcessW(app_path, command, NULL, NULL, FALSE, flags, NULL, directory,
                        &startup.StartupInfo, &child))
        goto cleanup;
    CloseHandle(child.hThread);
    DWORD result;
    if (WaitForSingleObject(child.hProcess, INFINITE) == WAIT_OBJECT_0 && GetExitCodeProcess(child.hProcess, &result))
        exit_code = result;
    CloseHandle(child.hProcess);

cleanup:
    if (attributes_initialized)
        DeleteProcThreadAttributeList(startup.lpAttributeList);
    if (startup.lpAttributeList)
        HeapFree(GetProcessHeap(), 0, startup.lpAttributeList);
    if (job)
        CloseHandle(job);
    return exit_code;
}

static DWORD run_launcher(void)
{
    PCWSTR original_command = GetCommandLineW();
    int argument_count;
    LPWSTR *arguments = CommandLineToArgvW(original_command, &argument_count);
    if (!arguments)
        return ERROR_INVALID_PARAMETER;
    BOOL maintenance = argument_count > 1 && is_maintenance_command(arguments[1]);
    LocalFree(arguments);

    BOOL protect;
    if (!prepare_launch(maintenance, &protect))
        return ERROR_ACCESS_DENIED;

    WCHAR directory[MAX_PATH], app_path[MAX_PATH];
    DWORD length = GetModuleFileNameW(NULL, directory, ARRAYSIZE(directory));
    if (!length || length >= ARRAYSIZE(directory))
        return ERROR_BAD_PATHNAME;
    while (length && directory[length - 1] != L'\\')
        --length;
    if (!length || length + ARRAYSIZE(app_name) > ARRAYSIZE(app_path))
        return ERROR_BAD_PATHNAME;
    directory[length] = L'\0';
    lstrcpyW(app_path, directory);
    lstrcpyW(app_path + length, app_name);

    // A fixed application name pins the executable. Preserve the original argument quoting.
    SIZE_T characters = (SIZE_T)lstrlenW(original_command) + 1;
    LPWSTR command = HeapAlloc(GetProcessHeap(), 0, characters * sizeof(WCHAR));
    if (!command)
        return ERROR_OUTOFMEMORY;
    lstrcpyW(command, original_command);
    DWORD result = start_app(command, app_path, directory, protect);
    HeapFree(GetProcessHeap(), 0, command);
    if (!maintenance && result == ERROR_GEN_FAILURE)
        MessageBoxW(NULL, L"Windows could not start the installed app. Repair the MSI installation and try again.",
                    title, MB_OK | MB_ICONERROR);
    return result;
}

void WINAPI LauncherEntry(void)
{
    ExitProcess(run_launcher());
}
