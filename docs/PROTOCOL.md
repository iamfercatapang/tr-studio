# Core Vision 360 screen protocol

Decoded from TRCC 2.1.4 and 2.1.8, and confirmed on a Core Vision 360 (mode 3) on 2026-10-31.

## Screen sizes

TRCC 2.1.8 picks the size from byte 24 of the handshake reply (the mode) and byte 36 (the sub mode).
Only mode 3 has been tested on real hardware. The rest come from TRCC's code.

| Mode | Size | Notes |
| --- | --- | --- |
| 3 and unlisted modes | 480x480 | Mode 3 is the round Core Vision |
| 63, 64, or 1 with sub mode 48 | 1600x720 | |
| 65, 66, or 1 with sub mode 49 | 1920x462 | |
| 69 | 1920x440 | Frame header says 1920x462 |
| 68 | 1280x480 | Sub mode 3 or higher only; header says 1920x462 |
| 9, 11, 20 | 854x480 | |
| 10, 16 | 960x540 | |
| 12 | 800x480 | |
| 13, 17, 18 | 960x320 | |
| 15 | 640x172 | |

- JPEG quality starts at 95, except 50 for mode 5 and 64 for modes 7 and 14.
- Not supported: mode 50 (RGB565 images) and mode 68 with sub mode 2 or lower (a 20-byte header).

## Other display families

These use different protocols, and the app only names them. No data is sent to them.

- USB 0416:5302, 0416:5406, 0416:5408 and 0416:5409 each have their own transfer loop in `USBLCDNEW`.
- TRCC also has HID (`UsbHid.dll`) and SCSI (`USBLCD.exe`) routes, which are not decoded.

## Connection

- USB 87AD:70DB, name `USBDISPLAY`, Windows `winusb.inf` driver.
- Bulk OUT endpoint 0x01, bulk IN endpoint 0x81.
- Only one process can hold the device. TRCC's `USBLCDNEW.exe` holds it while running.

## Handshake

Send 64 bytes: `12 34 56 78`, zeros, with byte 56 = `01`. The screen replies with 64 bytes.

| Reply bytes | Value on this unit | Meaning |
| --- | --- | --- |
| 0-3 | `12 34 56 78` | Magic |
| 4-11 | `SSCRM-V1` | Firmware family |
| 20-23 | `1E DB 3C 8F` | Identifier; meaning unconfirmed |
| 24 | 3 | TRCC's panel mode (`myDevicePingMu`) |
| 28 | 0x16 | Unconfirmed |
| 56 | 1 | Echo of the command |

TRCC treats this path as 480x480 and does not change the size for mode 3.

## Sending a frame

One bulk write of a 64-byte header followed by a baseline JPEG.

| Header bytes | Value |
| --- | --- |
| 0-3 | `12 34 56 78` |
| 4-7 | 2 (little-endian, picture command) |
| 8-11 | Width, 480 (little-endian) |
| 12-15 | Height, 480 (little-endian) |
| 56 | 2 |
| 60-63 | JPEG size (little-endian) |

- JPEG must be under 450,000 bytes. TRCC starts at quality 95 and lowers it by 5 when a frame is too large.
- If the total length is a multiple of 512, send a zero-length write afterwards.

## Not yet confirmed

- Brightness. `USBLCDNEW` defines commands 6 (rotate), 7 (screen) and 8 (backlight), but they have not been tested.
- Rotation is done in software, as TRCC does: the app turns each image before sending it.
- How the screen behaves if frames stop or after sleep.
