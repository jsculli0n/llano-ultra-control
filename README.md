# Llano Ultra Control

Llano Ultra Control is an MIT-licensed Windows 11 x64 app for live component readings, custom temperature-to-RPM profiles, and USB fan and lighting control for the Llano V12 Ultra.

Llano Ultra Control is an independent project unaffiliated with llano. It controls the external cooling pad. Your laptop manages its internal fans and thermal protection.

The source is available under the [MIT licence](LICENSE). Public 1.0 downloads are pending clean-machine and hardware shutdown verification. You can build the app from source using the commands below.

## Hardware support

The app uses the Windows HID driver to read and control revision `1601` of the V12 Ultra, USB ID `374A:B101` and usage `FF00/0001`. Live USB controls are available when one matching, powered-on cooler responds. Myth.Cool is unnecessary at runtime.

## Use

Extract the portable ZIP to a folder and open `LlanoUltraControl.exe`. Keep the files together. Use the MSI if you want a Start menu shortcut and sign-in startup options. Connect the cooler’s power adapter and USB data cable, turn it on, then choose a mode on **Cooling** and select **Apply**.

The public ZIP and MSI are unsigned. Windows may warn about an unrecognised download or block it under Smart App Control or a managed security policy. Check the download source and compare the SHA-256 hash with `SHA256SUMS.txt` from the same release. Hashes verify the file against the published value. [Microsoft SmartScreen guidance](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation)

```powershell
Get-FileHash .\LlanoUltraControl-1.0.0-win-x64-portable.zip -Algorithm SHA256
```

- **Cooling** shows the fan setting, CPU and GPU readings, the current fan response, and up to 60 seconds of speed history. Choose **Quiet**, **Balanced**, **Boost**, or **Manual** and click **Apply**. The automatic presets smooth temperature spikes and use whichever of the CPU and GPU curves calls for more airflow. A normal ramp increases by up to 200 RPM every two seconds. A decrease starts after 15 seconds of sustained lower demand, then steps down by 100 RPM every three seconds. Sustained high temperatures speed up the response, and extreme temperatures or losing both temperature inputs after startup trigger maximum cooling. Moving the physical dial pauses software fan control.
- **Custom curves** offers saved profiles with fixed RPM, a shared curve, or separate CPU and GPU curves. Edit the rows to update the chart, then use **Test temperature** to preview the resulting RPM without changing the fan. **Start from a preset** loads Quiet, Balanced, or Boost into the draft. A custom curve needs all its selected sensors and falls back to the profile maximum if it loses one during a session.
- **Sensors** shows the CPU and GPU inputs automatic cooling uses. The table starts with temperatures and supports search by component, hardware, or sensor name. Clear **Temperatures only** for other readings. Some platforms need the optional [PawnIO driver](https://pawnio.eu/) and an administrator launch. An existing HWiNFO installation can supply CPU and GPU temperatures with **Shared Memory Support** enabled and its sensors running.
- **Lighting** has eleven presets including **Low red**, **Blue pulse**, and **Rainbow chase**. Adjust brightness beside the preview, or expand **Customize lighting** for effects, speed, and the cooler’s five built-in colors. **Load from cooler** copies the device’s current lighting into the preview. **Apply to cooler** sends the chosen look. The preview is approximate because firmware controls the LED colors and patterns.
- **Settings → Cooler & USB** lets you turn the device on or off over USB. **Turn off the cooler when Windows shuts down** is an opt-in preference that also applies to restart. Windows must give the running app time to reach the cooler before USB disconnects. A sudden loss of laptop power can leave the externally powered cooler running. If you cancel shutdown after the command was sent, use **Turn cooler on** or the physical power button.
- **Quiet** starts at 300 RPM at light loads. The Cooling page displays the running mode separately from the selection you are editing. Apply becomes available when you select another mode, change Manual RPM, or pause cooling.
- **Settings → General** changes the app theme, Windows startup, and what closing the window does. The window closes to the notification area by default. An installed copy can opt to resume the last applied cooling mode at sign-in. For custom curves, sign-in uses the saved profile. Settings stay under `%LOCALAPPDATA%\LlanoUltraControl`.
- **Settings → Diagnostics** shows sensor sources, enables **Simulation mode**, and captures passive USB reports. On **Cooling**, expand **Simulation inputs** to adjust temperatures or simulate sensor loss. **Copy diagnostics** writes to your clipboard for you to review and share.

Llano describes Myth.Cool’s AI mode as temperature-driven control with three levels. This app’s automatic cooling uses its own local CPU and GPU curves. [Llano V12 Ultra](https://llanolife.com/products/llano-v12-ultra-rgb-laptop-cooling-pad)

## Troubleshooting

- **USB disconnected.** Reconnect the cooler’s USB data cable at both ends. If needed, try a different laptop port and connect directly for testing.
- **Cooler off.** Go to **Settings → Cooler & USB**, select **Turn cooler on**, or hold the cooler’s power button. Power changes can interrupt devices plugged into the cooler’s USB hub. Eject attached storage before changing its power state.
- **Temperature unavailable.** Check **Settings → Diagnostics** for the current sensor source. Automatic cooling can use one temperature source. Manual speed works without laptop temperature readings when the cooler responds over USB.
- **Upgrading.** Exit the old app from its tray icon before opening the new portable build or upgrading with the MSI. Settings are shared between builds.

## Build and verify

Install the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) and run these commands from the repository in PowerShell.

```powershell
.\build.cmd
.\build.cmd --portable
.\build.cmd --installer
```

The first command builds the app and checks the curve engine and settings. The second creates a self-contained x64 build in `artifacts\publish`. The third also builds the MSI in `artifacts` using WiX. Start simulation mode directly with `.\artifacts\publish\LlanoUltraControl.exe --demo`.

Set `LLANO_PUBLISH_DIR` before building to publish to a different folder, for example when the existing portable build is running.

Run `.\build.cmd --release` to package the portable ZIP, MSI and SHA-256 checksums in `artifacts\release-1.0.0`. The release build collects licence files from the runtime packages actually included in the app.

The check harness also supports live, read-only verification.

```powershell
dotnet run --project tests/Llano.Checks -- --probe
dotnet run --project tests/Llano.Checks -- --listen
```

See [USB protocol verification](docs/usb-protocol.md) for the report layout and hardware checks.

## Reporting a problem

In the GitHub repository, open an issue with your Windows version, cooler model and revision, app version, and the steps to reproduce the problem. **Settings → Diagnostics → Copy diagnostics** gives you USB and sensor information to review before sharing.
