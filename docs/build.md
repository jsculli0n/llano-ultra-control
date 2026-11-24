# Build from source

Use Windows 11 x64 and the latest serviced [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). Self-contained builds ship the .NET and Windows Desktop runtimes selected by that SDK.

```powershell
.\build.cmd
.\build.cmd --portable
.\build.cmd --installer
.\build.cmd --release
```

| Command | Output |
| --- | --- |
| `.\build.cmd` | Builds the app and runs the controller, settings, history, USB protocol, and FPS parsing checks. |
| `.\build.cmd --portable` | Publishes a self-contained build in `artifacts\publish`. |
| `.\build.cmd --installer` | Also builds the MSI with WiX. |
| `.\build.cmd --release` | Packages the ZIP, MSI, notices, and checksums in `artifacts\release-<version>`. |

Run `.\artifacts\publish\LlanoUltraControl.exe --demo` to use simulation. Set `LLANO_PUBLISH_DIR` to publish into a different folder.

The installer build compiles the native Windows launcher using a pinned llvm-mingw toolchain. The first installer build verifies the download against SHA-256 and caches it in `.native-tools`. The standard app build and the portable ZIP build use the .NET SDK alone.

## Dependency checks

Restore audits direct and transitive NuGet packages. Run these checks before packaging.

```powershell
dotnet package list --project src/Llano.App/Llano.App.csproj --include-transitive --vulnerable
dotnet package list --project installer/Llano.Installer.wixproj --include-transitive --vulnerable
dotnet package list --project installer/RemovePreferences/RemovePreferences.csproj --include-transitive --vulnerable
```

PresentMon is downloaded separately. Packaging verifies the executable and licence against their pinned SHA-256 values.

## Hardware checks

```powershell
dotnet run --project tests/Llano.Checks -- --probe
dotnet run --project tests/Llano.Checks -- --listen
```

These commands are read-only. See the [USB protocol](usb-protocol.md) and [release verification checklist](release-verification.md).
