# Release notes

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
