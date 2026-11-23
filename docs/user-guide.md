# User guide

## Run the app

Get the portable ZIP or MSI from the [1.0.0 release](https://github.com/jsculli0n/llano-ultra-control/releases/tag/v1.0.0). Extract the portable ZIP before opening `LlanoUltraControl.exe` and keep the files together. The MSI adds a Start menu shortcut and optional sign-in startup. Exit an older running version from the tray before switching builds.

Connect the cooler’s power adapter and USB data cable. Turn the cooler on and choose a mode on **Cooling**. Select **Apply** to start software cooling. Cooling starts paused on a fresh setup.

## Cooling and lighting

- **Quiet** starts at 300 RPM at light loads. **Balanced** responds to sustained heat. **Boost** provides more airflow. **Manual** holds the selected speed. Automatic modes use whichever CPU or GPU curve asks for more airflow.
- **Custom curves** supports shared or separate CPU and GPU curves and fixed speeds. Use **Test temperature** before applying a curve. **Start from a preset** loads Quiet, Balanced, or Boost into the draft.
- **Lighting** offers presets and a preview. Use **Load from cooler** to copy its current settings, adjust brightness or open **Customize lighting**, then select **Apply to cooler**. The firmware provides five colours and four effects.
- Moving the physical dial pauses software fan control. If all required sensors go stale after cooling starts, the controller uses the configured maximum.

## Settings and troubleshooting

- **USB disconnected.** Reconnect the USB data cable at both ends. Try another laptop port or connect directly for testing.
- **Cooler off.** In **Settings → Cooler & USB**, choose **Turn cooler on** or hold the cooler’s physical power button. Power changes may interrupt devices attached to the cooler’s USB hub. Eject attached storage before changing power.
- **Temperature unavailable.** Check **Settings → Diagnostics** for the current sensor source. Some laptops need optional [PawnIO](https://pawnio.eu/) and an administrator launch, or an existing HWiNFO installation with **Shared Memory Support** enabled. Manual speed still works when the cooler is reachable over USB.
- **Turn off the cooler when Windows shuts down.** This is off by default and applies to restart too. The app must be running and reach the cooler before Windows disconnects USB. The power command was tested directly on the cooler. End-to-end Windows shutdown and restart were not tested for this release.
- **Themes and tray.** Switch themes or change what closing the window does under **Settings → General**. The app closes to the tray by default. Use the tray menu to reopen it or exit.
- **Simulation.** Enable it in **Settings → Diagnostics** and use **Simulation inputs** on Cooling. The app reads USB status without writing settings while in simulation.

## Verify the download

The 1.0.0 ZIP and MSI are unsigned. Windows may display a warning or block them under a local security policy. Download from the project’s release and compare the SHA-256 value with `SHA256SUMS.txt`. [Microsoft SmartScreen guidance](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation)

```powershell
Get-FileHash .\LlanoUltraControl-1.0.0-win-x64-portable.zip -Algorithm SHA256
```

## Get support

[Open a GitHub issue](https://github.com/jsculli0n/llano-ultra-control/issues/new) with your Windows version, cooler model and revision, the app version, and steps to reproduce the issue. **Settings → Diagnostics → Copy diagnostics** copies USB and sensor information to your clipboard for you to review before sharing.
