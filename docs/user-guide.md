# User guide

## Run the app

Get the portable ZIP or MSI from the [latest release](https://github.com/jsculli0n/llano-ultra-control/releases/latest). Extract the portable ZIP before opening `LlanoUltraControl.exe` and keep the files together. Exit an older running version from the notification area before switching builds.

The MSI adds a Start menu shortcut. In setup, select **Create a desktop shortcut** if you want one. Leave **Open Llano Ultra Control** selected on the last screen to launch the app when you select **Finish**. Windows asks for administrator approval to install the MSI. The app opens with standard permissions. Use the **Sensors** tab if you need to restart the app with administrator access.

Connect the cooler’s power adapter and USB data cable. Turn the cooler on and choose a mode on **Cooling**. Select **Apply** to start software cooling. Cooling starts paused on a fresh setup.

## Cooling and lighting

- **Quiet** starts at 300 RPM at light loads. **Balanced** responds to sustained heat. **Boost** provides more airflow. These presets require a CPU temperature. They also respond to the GPU when its temperature is available. **Manual** holds the selected speed.
- **Custom curves** supports shared or separate CPU and GPU curves and fixed speeds. Use **Test temperature** before applying a curve. **Start from a preset** loads Quiet, Balanced, or Boost into the draft.
- **Lighting** offers presets and a preview. Use **Load from cooler** to copy its current settings, adjust brightness or open **Customize lighting**, then select **Apply to cooler**. The firmware provides five colours and four effects.
- Moving the physical dial pauses software fan control. If the CPU reading is lost during an automatic preset, or a required reading is lost during a custom curve, the controller uses the configured maximum until the readings return.

## Settings and troubleshooting

- **USB disconnected.** Reconnect the USB data cable at both ends. Try another laptop port or connect directly for testing.
- **Cooler off.** In **Settings → Cooler & USB**, choose **Turn cooler on** or hold the cooler’s physical power button. Power changes may interrupt devices attached to the cooler’s USB hub. Eject attached storage before changing power.
- **CPU temperature unavailable.** On **Sensors**, select **Restart as administrator** and approve the Windows prompt. Some laptops also need optional [PawnIO](https://pawnio.eu/) or can get their readings from HWiNFO with **Shared Memory Support** enabled. Check **Settings → Diagnostics** for the current sensor source. Manual speed still works when the cooler is reachable over USB.
- **Administrator access on launch.** In **Settings → General**, turn on **Request administrator access when I open the app**. Windows prompts when you open a new session. If you cancel, the app opens with standard permissions. The **Windows startup** option controls the separate sign-in launch.
- **Resume cooling at sign-in.** Temperature-based profiles wait for five seconds of fresh readings before taking control. A preset still responds to sustained critical heat.
- **Updates.** Select **Settings → About → Check for updates**. If a newer version is available, open the GitHub release page to download it.
- **Turn off the cooler when Windows shuts down.** This is off by default and applies to restart too. The app must be running and reach the cooler before Windows disconnects USB. The power command was tested directly on the cooler. End-to-end Windows shutdown and restart were not tested for this release.
- **Themes and tray.** Switch themes or change what closing the window does under **Settings → General**. The app closes to the tray by default. Use the tray menu to reopen it or exit.
- **Simulation.** Enable it in **Settings → Diagnostics** and use **Simulation inputs** on Cooling. The app reads USB status without writing settings while in simulation.

## Verify the download

The ZIP and MSI are unsigned. Windows may display a warning or block them under a local security policy. Download from the project’s release and compare the SHA-256 value with `SHA256SUMS.txt`. [Microsoft SmartScreen guidance](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation)

```powershell
Get-FileHash .\LlanoUltraControl-1.0.4-win-x64-portable.zip -Algorithm SHA256
```

## Uninstall

If Llano Ultra Control is running, right-click its notification area icon and select **Exit**. Open **Windows Settings → Apps → Installed apps** and uninstall **Llano Ultra Control**. Uninstall removes the app, its shortcuts, its sign-in entries, and the saved profiles and preferences for the Windows account running uninstall. Installing a newer version preserves those settings.

For the portable version, exit the app and delete its extracted files and `%LOCALAPPDATA%\LlanoUltraControl\settings.json`.

## Get support

[Open a GitHub issue](https://github.com/jsculli0n/llano-ultra-control/issues/new) with your Windows version, cooler model and revision, the app version, and steps to reproduce the issue. **Settings → Diagnostics → Copy diagnostics** copies USB and sensor information to your clipboard for you to review before sharing.
