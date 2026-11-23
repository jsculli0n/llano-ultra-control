# V12 Ultra USB protocol

The verified Windows HID device uses USB ID `374A:B101`, revision `1601`, usage page `FF00` and usage `0001`. It advertises 65-byte input and output reports and nine-byte feature reports, including the Windows report ID byte. The device can be discovered and controlled with the built-in HID driver.

## Status and setting reports

Read the current state with a HID feature read for report ID 0. Writes use a HID feature write with the same report ID.

| Offset | Status reply | Setting command |
| --- | --- | --- |
| 0 | Windows report ID 0 | Windows report ID 0 |
| 1 | `0x80` software mode, `0x88` dial mode | `1` enter software mode, `0` preserve mode |
| 2 | Speed level | Speed level |
| 3 | Power state, `0` on and `1` off | Preserve power unless sending an explicit on/off command |
| 4 | Effect index in bits 0–6, lights off in bit 7 | Same format |
| 5 | Color index, 0–4 | Same format |
| 6 | Lighting effect speed, 0–3 | Same format or preserve the current value |
| 7 | Brightness byte | Brightness byte |
| 8 | Check byte | Complement of the low byte of the preceding sum |

The dial displayed 800 and 1,200 RPM at levels 20 and 36. The mapping is `300 + 25 × level` RPM, with 100 RPM steps used by the app. The speed field reports the cooler’s setting. Brightness uses a byte from 0 to 255, displayed as 0–100%. Color is a firmware palette index. The device generates the gradient and chase patterns.

## Verification

- Static inspection of the Llano installer identified its USB Center dependency. The publisher’s USB Center package supplied the command layout. [GamePP Lite package manifest](https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/g/GamePP/GamePPLite/6.1.176.725/GamePP.GamePPLite.installer.yaml)
- On revision `1601`, a mode-preserving command toggled lighting while the dial owned the speed. Mode `1` took software control and raised the setting from 1,200 to 1,300 RPM. A subsequent mode-preserving command changed the software speed and brightness. The original settings were restored.
- Moving the dial returned the report to `0x88`. The app stops writing when this occurs during software control. Pause and Exit stop software writes. The cooler holds its most recent speed setting until a new setting is applied.
- A mode-preserving power-off command returned an off status while the HID interface stayed connected. A power-on command restored and confirmed the original 1,000 RPM setting and lighting.
