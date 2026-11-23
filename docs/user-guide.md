# User guide

## Run the app

Get the portable ZIP or MSI from the [latest release](https://github.com/jsculli0n/llano-ultra-control/releases/latest). Extract the portable ZIP before opening `LlanoUltraControl.exe` and keep the files together. Exit an older running version from the notification area before switching builds.

The MSI adds a Start menu shortcut. In setup, select **Create a desktop shortcut** if you want one. Leave **Open Llano Ultra Control** selected on the last screen to launch the app when you select **Finish**. Windows asks for administrator approval to install the MSI. The app opens with standard permissions.

Connect the cooler’s power adapter and USB data cable. Turn the cooler on and choose a mode on **Cooling**. Select **Apply** to start software cooling. Cooling starts paused on a fresh setup.

## Cooling and lighting

- **Quiet** starts at 300 RPM at light loads. **Balanced** responds to sustained heat. **Boost** provides more airflow. These presets require a CPU temperature. They also respond to the GPU when its temperature is available. **Manual** holds the selected speed.
- **Custom curves** supports shared or separate CPU and GPU curves and fixed speeds. Use **Test temperature** before applying a curve. **Start from a preset** loads Quiet, Balanced, or Boost into the draft.
- **Cooling history** shows cooler RPM and CPU/GPU temperature together. Select a time range from 60 seconds to 24 hours and hover over the chart for individual readings. Longer history is stored locally while the app is running. The latest five minutes have higher detail during the current session.
- **Lighting** offers presets and a preview. Use **Load from cooler** to copy its current settings, adjust brightness or open **Customize lighting**, then select **Apply to cooler**. The firmware provides five colours and four effects.
- Moving the physical dial pauses software fan control. If the CPU reading is lost during an automatic preset, or a required reading is lost during a custom curve, the controller uses the configured maximum until the readings return.

## Settings and troubleshooting

- **USB disconnected.** Reconnect the USB data cable at both ends. Try another laptop port or connect directly for testing.
- **Cooler off.** In **Settings → Cooler & USB**, choose **Turn cooler on** or hold the cooler’s physical power button. Power changes may interrupt devices attached to the cooler’s USB hub. Eject attached storage before changing power.
- **Sensor and FPS access.** In the MSI-installed app, select **Settings → General → Enable sensor and FPS access** and approve once from your Windows administrator account. The helper starts when the app requests CPU temperature, CPU clock, or FPS and exits when the app releases it. Opening the app and starting at sign-in use standard permissions. Older elevated sign-in setups switch to the helper at sign-in and retain sensor access. Use the same setting to revoke access.
- **CPU temperature unavailable.** Enable sensor and FPS access, then check **Settings → Diagnostics** for the sensor source. Some laptops need optional [PawnIO](https://pawnio.eu/) or can get temperature readings from HWiNFO with **Shared Memory Support** enabled. Manual speed works when the cooler is reachable over USB.
- **Resume cooling at sign-in.** Temperature-based profiles wait for five seconds of fresh readings before taking control. A preset still responds to sustained critical heat.
- **Updates.** Select **Settings → About → Check for updates**. If a newer version is available, open the GitHub release page to download it.
- **Desktop stats.** Go to **Settings → Stats overlay** to choose grouped readings, opacity, colours, position, layout, and text size. The click-through overlay stays visible while the app is in the notification area. **FPS** uses the bundled PresentMon console helper for the foreground app and may need administrator access. CPU clock uses the Windows-reported frequency when a hardware reading is unavailable.
- **Turn the cooler off at shutdown and on at sign-in.** This is off by default and also applies when restarting Windows. Keep the app running for shutdown and enable **Start in tray at sign-in** under **Settings → General** so the app can turn the cooler on. The app waits up to 45 seconds for the cooler to respond over USB after sign-in.
- **Themes and tray.** Switch themes or change what closing the window does under **Settings → General**. The app closes to the tray by default. Use the tray menu to reopen it or exit.
- **Simulation.** Enable it in **Settings → Diagnostics** and use **Simulation inputs** on Cooling. The app reads USB status without writing settings while in simulation.

## Verify the download

The ZIP and MSI are unsigned. Windows may display a warning or block them under a local security policy. Download from the project’s release and compare the SHA-256 value with `SHA256SUMS.txt`. [Microsoft SmartScreen guidance](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation)

```powershell
Get-FileHash .\LlanoUltraControl-1.0.6-win-x64-portable.zip -Algorithm SHA256
```

## Uninstall

If Llano Ultra Control is running, right-click its notification area icon and select **Exit**. Open **Windows Settings → Apps → Installed apps** and uninstall **Llano Ultra Control**. Uninstall removes the app, its shortcuts, its sign-in entries, and the saved profiles and preferences for the Windows account running uninstall. Installing a newer version preserves those settings.

For the portable version, exit the app and delete its extracted files. To remove local settings and chart data, delete `settings.json` and `cooling-history.bin` from `%LOCALAPPDATA%\LlanoUltraControl`.

## Get support

[Open a GitHub issue](https://github.com/jsculli0n/llano-ultra-control/issues/new) with your Windows version, cooler model and revision, the app version, and steps to reproduce the issue. **Settings → Diagnostics → Copy diagnostics** copies USB and sensor information to your clipboard for you to review before sharing.
