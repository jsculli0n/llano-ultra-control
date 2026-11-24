#include <windows.h>
#include <shellapi.h>
#include <shlobj.h>

static BOOL has_value(PCWSTR name, PCWSTR expected)
{
    WCHAR value[MAX_PATH];
    DWORD size = GetEnvironmentVariableW(name, value, ARRAYSIZE(value));
    return size && size < ARRAYSIZE(value) && lstrcmpW(value, expected) == 0;
}

static DWORD check_launch(int count, LPWSTR *arguments)
{
    if (count != 4 || lstrcmpW(arguments[3], L"argument with spaces") != 0)
        return 49;
    if (GetFileAttributesW(L"launcher-check.marker") == INVALID_FILE_ATTRIBUTES)
        return 50;

    BOOL check_profile = lstrcmpW(arguments[2], L"clean-profile") == 0;
    if (check_profile || lstrcmpW(arguments[2], L"clean") == 0)
    {
        WCHAR windows[MAX_PATH], system[MAX_PATH], ignored[2];
        if (GetEnvironmentVariableW(L"LLANO_LAUNCHER_CHECK", ignored, ARRAYSIZE(ignored)) != 0 ||
            GetLastError() != ERROR_ENVVAR_NOT_FOUND)
            return 51;
        if (!GetSystemWindowsDirectoryW(windows, ARRAYSIZE(windows)) ||
            !GetSystemDirectoryW(system, ARRAYSIZE(system)) ||
            !has_value(L"SystemRoot", windows) || !has_value(L"windir", windows) ||
            !has_value(L"PATH", system) || !has_value(L"DOTNET_EnableDiagnostics", L"0"))
            return 52;
        if (check_profile)
        {
            WCHAR local_data[MAX_PATH];
            if (FAILED(SHGetFolderPathW(NULL, CSIDL_LOCAL_APPDATA, NULL, SHGFP_TYPE_CURRENT, local_data)) ||
                GetFileAttributesW(local_data) == INVALID_FILE_ATTRIBUTES)
                return 54;
        }
    }
    else if (!has_value(L"LLANO_LAUNCHER_CHECK", L"caller environment") ||
             !has_value(L"DOTNET_EnableDiagnostics", L"1"))
        return 53;

    return 42;
}

void WINAPI ProbeEntry(void)
{
    int count;
    LPWSTR *arguments = CommandLineToArgvW(GetCommandLineW(), &count);
    if (!arguments)
        ExitProcess(48);
    DWORD result = check_launch(count, arguments);
    LocalFree(arguments);
    ExitProcess(result);
}
