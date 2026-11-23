# User guide

## Run the app

Get the portable ZIP or MSI from the [latest release](https://github.com/jsculli0n/llano-ultra-control/releases/latest). Extract the portable ZIP before opening `LlanoUltraControl.exe` and keep the files together. Exit an older running version from the notification area before switching builds.

The MSI adds a Start menu shortcut. In setup, select **Create a desktop shortcut** if you want one. Leave **Open Llano Ultra Control** selected on the last screen to launch the app when you select **Finish**. Windows asks for administrator approval to install the MSI. The app opens with standard permissions.

Connect the cooler’s power adapter and USB data cable. Turn the cooler on and choose a mode on **Cooling**. Select **Apply** to start software cooling. Cooling starts paused on a fresh setup.

## Cooling and lighting

- **Quiet** starts at 300 RPM at light loads. **Balanced** responds to sustained heat. **Boost** provides more airflow. **Manual** holds the selected speed. Automatic modes use whichever CPU or GPU curve asks for more airflow.
- **Custom curves** supports shared or separate CPU and GPU curves and fixed speeds. Use **Test temperature** before applying a curve. **Start from a preset** loads Quiet, Balanced, or Boost into the draft.
- **Lighting** offers presets and a preview. Use **Load from cooler** to copy its current settings, adjust brightness or open **Customize lighting**, then select **Apply to cooler**. The firmware provides five colours and four effects.
- Moving the physical dial pauses software fan control. If all required sensors go stale after cooling starts, the controller uses the configured maximum.

## Settings and troubleshooting

- **USB disconnected.** Reconnect the USB data cable at both ends. Try another laptop port or connect directly for testing.
- **Cooler off.** In **Settings → Cooler & USB**, choose **Turn cooler on** or hold the cooler’s physical power button. Power changes may interrupt devices attached to the cooler’s USB hub. Eject attached storage before changing power.
- **CPU temperature unavailable.** On **Sensors**, select **Restart as administrator** and approve the Windows prompt. Some laptops also need optional [PawnIO](https://pawnio.eu/) or can get their readings from HWiNFO with **Shared Memory Support** enabled. Check **Settings → Diagnostics** for the current sensor source. Manual speed still works when the cooler is reachable over USB.
- **Turn off the cooler when Windows shuts down.** This is off by default and applies to restart too. The app must be running and reach the cooler before Windows disconnects USB. The power command was tested directly on the cooler. End-to-end Windows shutdown and restart were not tested for this release.
- **Themes and tray.** Switch themes or change what closing the window does under **Settings → General**. The app closes to the tray by default. Use the tray menu to reopen it or exit.
- **Simulation.** Enable it in **Settings → Diagnostics** and use **Simulation inputs** on Cooling. The app reads USB status without writing settings while in simulation.

## Verify the download

The ZIP and MSI are unsigned. Windows may display a warning or block them under a local security policy. Download from the project’s release and compare the SHA-256 value with `SHA256SUMS.txt`. [Microsoft SmartScreen guidance](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation)

```powershell
Get-FileHash .\LlanoUltraControl-1.0.2-win-x64-portable.zip -Algorithm SHA256
```

## Get support

[Open a GitHub issue](https://github.com/jsculli0n/llano-ultra-control/issues/new) with your Windows version, cooler model and revision, the app version, and steps to reproduce the issue. **Settings → Diagnostics → Copy diagnostics** copies USB and sensor information to your clipboard for you to review before sharing.
