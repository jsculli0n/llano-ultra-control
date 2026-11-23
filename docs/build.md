# Build from source

Use Windows 11 x64 and the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).

```powershell
.\build.cmd
.\build.cmd --portable
.\build.cmd --installer
.\build.cmd --release
```

| Command | Output |
| --- | --- |
| `.\build.cmd` | Builds the app and runs the controller, settings, and USB protocol checks. |
| `.\build.cmd --portable` | Publishes a self-contained build in `artifacts\publish`. |
| `.\build.cmd --installer` | Also builds the MSI with WiX. |
| `.\build.cmd --release` | Packages the ZIP, MSI, notices, and checksums in `artifacts\release-<version>`. |

Run `.\artifacts\publish\LlanoUltraControl.exe --demo` to use simulation. Set `LLANO_PUBLISH_DIR` to publish into a different folder.

## Hardware checks

```powershell
dotnet run --project tests/Llano.Checks -- --probe
dotnet run --project tests/Llano.Checks -- --listen
```

These commands are read-only. See the [USB protocol](usb-protocol.md) and [release verification checklist](release-verification.md).
