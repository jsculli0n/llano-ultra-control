using System;
using System.IO;
using System.Security;
using System.Security.Principal;
using Llano.Core;

internal static class Program
{
    private static int Main()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (identity.IsSystem || identity.IsAnonymous) return 0;

        try
        {
            AppDataFiles.RemoveSettings(AppDataFiles.DirectoryPath);
            return 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
        {
            return 1;
        }
    }
}
