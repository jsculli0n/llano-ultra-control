# 1.0 release verification

Use a clean Windows 11 x64 test machine or disposable VM. Keep a test copy of the 0.7.0 MSI and settings for the upgrade test. Allow time for the cooler to reconnect on each boot.

## Downloads and installation

- Compare the ZIP and MSI hashes against `SHA256SUMS.txt`. The ZIP and MSI are unsigned.
- Extract the ZIP and launch it as a standard user without a separately installed .NET runtime. Check that cooling starts paused, startup is described as MSI-only, and the About documents open.
- Install the MSI. Check the Start menu shortcut, version in About, and standard startup at sign-in. Verify that granting administrator startup is optional and can be undone.
- Install 0.7.0, create a named profile, choose a startup preference, and upgrade. Verify that the newer version preserves the profile and startup preference. The theme should default to Ember Red.
- Uninstall and confirm the shortcuts, installed app, scheduled startup tasks, and the uninstalling account’s saved profiles and preferences are removed. Reinstall and check that it starts with the default settings.
- In the MSI-installed app, set up the telemetry helper and approve the Windows prompt. Exit, reopen with standard permissions, and verify the helper connects without another prompt. Verify FPS where Windows permits capture and CPU readings on a system with a supported sensor driver. Disable the helper and verify the task is removed.

## Hardware and shutdown

- On the V12 Ultra revision 1601, confirm fan speed, lighting, USB power, and manual dial override. Disconnect and reconnect the data cable while cooling is enabled, then confirm the app recovers with fresh readings.
- Connect a disposable keyboard through the cooler. Leave storage disconnected for this test. Verify keyboard input before and after restarting Windows.
- With automatic cooler power disabled, shut down Windows and verify that the app sends no power command. With the setting and Windows sign-in startup enabled, test both shutdown and restart. Verify that the cooler turns off at shutdown, turns on at sign-in, and restores USB control and keyboard input. Turn the cooler off manually and verify that it stays off during the same session.
- Record the machine, cooler revision and observations. Document any interruption or failed test before publishing the release.
