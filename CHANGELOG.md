# Release notes

## 1.0.7

- Replaced the administrator launch options with one **Enable sensor and FPS access** setting in the MSI-installed app. Existing helper approvals carry over, and older elevated sign-in tasks migrate to standard startup with on-demand access.
- The desktop overlay now requests hardware CPU clock readings when needed, even if a CPU temperature is already available. The Windows frequency remains available as a fallback.

## 1.0.6

- Added an optional MSI-only telemetry helper. Approve it once to let the standard-permission app request supported CPU readings and FPS on demand.
- Added 24-hour local cooling history with selectable time ranges and CPU/GPU temperatures alongside cooler RPM.
- Added a configurable, click-through overlay with grouped CPU, GPU and memory readings, cooler RPM, and optional foreground-app FPS via PresentMon.
- Added background opacity, outline, and app-theme colours for the overlay. CPU clock uses Windows frequency when a hardware clock reading is unavailable.

## 1.0.5

- The shutdown power option now turns the cooler back on when the app starts at Windows sign-in. It waits for USB to respond and leaves the cooler alone after the sign-in attempt finishes.

## 1.0.4

- Sign-in cooling waits for five seconds of fresh temperature samples. Automatic presets confirm critical heat during startup before using the maximum RPM.
- Added a manual update check under **Settings → About**. New versions open on GitHub for download.

## 1.0.3

- Added an optional administrator request on launch. The app opens with standard permissions if you cancel the Windows prompt.
- Put the app icon in the window header and redesigned the lighting preview with distinct breathing, gradient, and chase animations.
- Quiet, Balanced, and Boost require a fresh CPU temperature before starting. If the CPU reading is lost while a preset is active, the preset uses its maximum speed until readings return.
- Fixed the installer flow that opens the app after setup.
- Added uninstall confirmation and completion screens. Uninstall removes the app, its shortcuts, its sign-in entries, and the uninstalling Windows account’s saved profiles and preferences. Upgrades preserve saved settings.
- Fixed Windows startup settings showing a missing-file error when a sign-in task had never been created. Settings now displays the app's permission level separately, and Sensors shows whether LibreHardwareMonitor detected a standalone PawnIO installation.

## 1.0.2

- Added a setup screen with an optional desktop shortcut and an option to open the app when setup finishes.
- Added the app icon to the Start menu shortcut and Windows Installed Apps.
- Added a restart-as-administrator action to Sensors when CPU temperature is unavailable.

## 1.0.1

- Fixed the MSI rejecting compatible Windows 11 x64 systems by reading the build number from the 64-bit Windows registry.

## 1.0.0

- First public release with automatic cooling presets, manual RPM, custom fan curves, sensor readings, and USB lighting and power control.
- Added Ember Red, Violet, Ice Blue, and Graphite themes with immediate switching and saved preferences.
- Reorganised Settings into General, Cooler & USB, Diagnostics, and About. Added a preference for closing to the tray.
- Added recovery links for an unresponsive, disconnected, or powered-off cooler and for unavailable temperature readings.
- Reopening the app brings its existing 1.0 window forward.
- Included separate self-contained Windows x64 portable ZIP and MSI downloads with SHA-256 hashes.

### Compatibility

Windows 11 x64. USB fan, lighting, and power control verified on the Llano V12 Ultra revision `1601` with USB ID `374A:B101`. Laptop sensors vary by model and driver. The cooler’s power command was tested directly. Clean-machine installation and end-to-end Windows shutdown tests were not performed for this release. Power changes may interrupt the cooler’s USB hub.

The release files are unsigned. Windows may display a download warning or block execution under its security policy. See the README for verification and installation steps.
