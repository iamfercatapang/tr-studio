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

Besides the Core Vision family, the app drives Lovingcool screens (see below) and four families experimentally, decoded from TRCC 2.1.4 without the screens. It only names the rest and sends them nothing.

- 0416:5408 (Trofeo Vision 9.16"), TRCC's "LY": decoded below, driven by `TrofeoProtocol`.
- 0416:5302, TRCC's HID screens: decoded below, driven by `HidLcdProtocol` over HID.
- 0416:5406, TRCC's "ALi" screens: decoded below, driven by `AliLcdProtocol` over WinUSB.
- 87CD:70DB and 0402:3922, TRCC's SCSI screens (`USBLCD.exe`): decoded below, driven by `ScsiLcdProtocol` with SCSI commands to the USB drive.
- Not driven: 0416:5409 ("LY1", no known product, and its size table differs), 0418:5303 and 0418:5304 (TRCC 2.1.4 opens them but its send code for them is empty).
- The USB IDs and product names were cross-checked against the device list of the Linux project thermalright-trcc-linux (GPL-3.0); only those facts were taken from it, no code.

## Trofeo Vision (USB 0416:5408)

Decoded from `ThreadSendDeviceDataLY` in `USBLCDNEW.dll` of TRCC 2.1.2 and 2.1.4, whose code is identical.
TRCC calls this family "LY". **Nothing here has been tried on a real screen.**

### Lovingcool screens (USB 33C3:7788)

Written from the protocol facts documented by lovi-ctrl (github.com/newan0805/loving_cool_aio_display_manage), which reverse engineered the vendor's app. That repository has no license, so only facts were used, no code. Tested there on a 480x320 "txm818-7inch" unit; **not tried on real hardware here.** 33C3:7797 and 7791 may be variants and aren't driven.

- A USB serial (CDC) port at 2,000,000 baud with DTR on. The app finds its COM port from the `PortName` Windows keeps under the device's registry key.
- Commands: `55 AA`, the whole command's length (payload + 7) as a 16-bit little-endian number, the command byte, the payload, then the 16-bit little-endian sum of every byte before it.
- `0x06` asks for info. The reply is a command frame whose payload is JSON: `width`, `height`, `brightness`, `model`, `version`, `uid`, `region` (sometimes inside `data`).
- `0x11` starts live pictures, once per connection. No answer is read for it or for pictures.
- A picture is a JPEG of at most 50 KB, at the panel's size. Firmware whose version number is above 2.8 wants it wrapped: the JPEG's length (4 bytes, little-endian), the JPEG, then the 16-bit sum of those bytes. Older firmware (Ver1.0) takes the bare JPEG.
- Other commands: `0x01` restarts the screen, `0x03` sets brightness (one byte, 0 to 100). The app doesn't use them.

## HID screens (USB 0416:5302)

Decoded from `UCDevice` (`DeviceOnConnected2`, `DeviceDataReceived2`, `ThreadSendDeviceData2`) and `FormCZTV` (`FormCZTVInit`, `ImageToJpg`) in `TRCC.exe`, and `CommandMessage` in `UsbHid.dll`, TRCC 2.1.4. Products: Trofeo Vision LCD, Assassin Spirit 120 Vision, Frozen Warframe SE, 360 and Pro, AS120 and BA120 Vision. **Nothing here has been tried on a real screen.**

- Windows sees the screen as HID, so no driver is needed. Every report is 513 bytes: report id 0, then up to 512 bytes, zero-padded.
- Hello: `DA DB DC DD`, eight zeros, `01`, seven zeros (20 bytes).
- Reply (input report, report id first): bytes 1 to 4 are `DA DB DC DD`; byte 13 is 1 for the hello reply and 8 for a frame answer. Byte 6 is the mode, byte 5 the sub mode. When byte 17 is 16, bytes 21 to 36 are a serial.
- Mode to size: 72 or 129 is 480x480, 54 is 360x360, 128 is 1280x480, 100 to 102 is 320x320, 64 is 640x480, 36 or 37 is 240x240, 58 and anything else is 320x240. Mode 49 sends RGB565 and isn't supported.
- Frame: a header, then the JPEG, cut into 512-byte reports. TRCC doesn't wait for the answers.
  - 480x480, 360x360, 1280x480 and the default 320x240 use a 20-byte header: `DA DB DC DD`, `02 00 00 00`, width and height as 16-bit numbers, `02 00 00 00`, the JPEG length as a 32-bit number. The default 320x240 header names the picture 240 wide and 320 tall, so the app turns the picture a quarter turn.
  - 320x320, 640x480, 240x240 and mode 58's 320x240 use the 64-byte Core Vision header below.

## ALi screens (USB 0416:5406)

Decoded from `ThreadSendDeviceDataALi` in `USBLCDNEW.dll` and `FormCZTV.ImageTo565` in `TRCC.exe`, TRCC 2.1.4. Products: LC1, LC2, LC3 and LC5 pump heads. **Nothing here has been tried on a real screen.**

- WinUSB bulk, OUT endpoint 0x02, IN 0x81.
- Hello: `F5 00 01 00 BC FF B6 C8`, four zeros, then 1024 as a 32-bit number, followed by 1024 zero bytes.
- Reply: byte 0 is 54 (320x240), 101 or 102 (320x320); TRCC's mode is that minus one. Bytes 10 to 13 are a serial.
- Frame: `F5 01 01 00 BC FF B6 C8`, four zeros, the pixel data length as a 32-bit number, then raw RGB565 pixels, high byte first (`RRRRRGGG GGGBBBBB`). One write, then a 16-byte answer is read.
- TRCC sends 320x240 panels high byte first only in its "SPI" mode; the app assumes high byte first for both sizes. If colors look wrong on a 320x240 ALi screen, this is the first thing to check.

## SCSI screens (USB 87CD:70DB and 0402:3922)

Decoded from TRCC 2.1.4's native `USBLCD.exe` (its frame loop, which reads the `shareMemory_Image` mapping, and its `TransUsb` class) and from `Form1` and `FormCZTV` (`FormCZTVInit`, `ImageTo565`) in `TRCC.exe`. Products: Frozen Horizon Pro, Frozen Magic Pro and Frozen Vision V2 (87CD:70DB), Frozen Warframe and Frozen Warframe 240 (0402:3922). **Nothing here has been tried on a real screen.**

### Connection

- Windows sees the screen as a USB drive. TRCC finds it by opening the drive letters `\\.\C:` to `\\.\Z:` in turn, sending a standard INQUIRY (`12 00 00 00 24 00`, 36 bytes in) and a vendor probe (CDB `F5 41 50 49 58 B3 0C` + zeros, "APIX", 12 bytes in) and keeping the drive whose probe answer starts `02 04`. The app instead takes the disk whose USB parent has the screen's VID and PID.
- Every command is `IOCTL_SCSI_PASS_THROUGH_DIRECT` (`0x4D014`) on that handle: CDB length 16, sense length 24, TargetId 1, 10-second timeout. It needs administrator rights.
- TRCC builds each command as 20 bytes, the 16-byte CDB plus a checksum of it, but only the first 16 bytes reach the screen.

### CDB

| Bytes | Value |
| --- | --- |
| 0 | `F5` (vendor opcode) |
| 1 | 0 = read from the screen, 1 = write to it |
| 2 | Command: 0 status / start, 1 frame (2, 3 and 4 store pictures on the screen; not used) |
| 3 | Chunk index from 0 |
| 4-11 | 0 |
| 12-15 | Data length (little-endian) |

### Hello

- Read `F5 00 00 00`, zeros, length 0xE100: 57,600 bytes come back.
- When bytes 4 to 7 are `A1 A2 A3 A4` the screen is still starting; TRCC waits 3 seconds and asks again. The app tries five times.
- Byte 0 is the panel code. `USBLCD.exe` hands TRCC only these five; it writes `DC` and the code into the last two bytes of the shared buffer's first slot (offsets 153598 and 153599), and TRCC reads the code from there.

| Code | Size | Pixel order |
| --- | --- | --- |
| 36 | 240x240 | low byte first |
| 50 | 320x240 | low byte first |
| 51 | 320x240 | high byte first (TRCC's "SPI mode 2") |
| 100, 101 | 320x320 | high byte first |

### Sending a frame

- Pixels are raw RGB565 (`RRRRRGGG GGGBBBBB`), no header. TRCC's `ImageTo565` puts the high byte first for codes 51, 100 and 101, and the low byte first otherwise.
- The 320x240 panels scan portrait: with rotation 0 TRCC turns the picture 90° clockwise (GDI+ `RotateAt(90)`) and sends 240 pixels a row, 320 rows. Square panels aren't turned.
- Before the first frame after connecting, write `F5 01 00 00` with length 0xE100, carrying the frame's first 0xE100 bytes.
- Then each frame goes out as writes `F5 01 01 <index>`:
  - 240x240 (115,200 bytes): 0xE100, 0xE100.
  - 320x240 (153,600 bytes): 0xE100, 0xE100, 0x9600.
  - 320x320 (204,800 bytes): 0x10000, 0x10000, 0x10000, 0x2000.
- Nothing is read back after a frame. TRCC sends a frame when `TRCC.exe` sets the third byte of the shared buffer's first slot (the 4 bytes `00 01 01 1E`, after writing the pixels to the second slot), and otherwise checks every 5 ms.

## Connection

- TRCC opens it with LibUsbDotNet: configuration 1, interface 0.
- Bulk OUT endpoint **0x09**, bulk IN endpoint 0x81. (The Core Vision loop passes 1 for its known OUT 0x01, so the value is the endpoint address.)
- Which Windows driver the screen uses (WinUSB or libusb) is unknown.

### Handshake

Write 2048 bytes: these 16, then zeros.

`02 FF 00 00 00 00 00 00 01 00 00 00 00 00 00 00`

Read 512 bytes. TRCC waits 200 ms, then cancels whichever transfer hasn't finished. The reply is good when byte 0 is 3, byte 1 is `FF` and byte 8 is 1.

| Reply bytes | Meaning |
| --- | --- |
| 16-19 | Serial, shown by TRCC as hex |
| 20 | Panel type. Values of 3 or less are treated as 1. The mode is 64 plus this. |
| 22 | The sub mode is 1 plus this |

Mode 65 or 66 is 1920x462 in the size table above. TRCC sends a JPEG, as for the Core Vision.

### Sending a frame

The JPEG is split into chunks of 496 bytes. There are always `size / 496 + 1` chunks, so a JPEG whose size is a multiple of 496 ends with an empty chunk.
Each chunk goes in a 512-byte packet:

| Packet bytes | Value |
| --- | --- |
| 0 | 1 |
| 1 | `FF` |
| 2-5 | JPEG size (little-endian) |
| 6-7 | Bytes of JPEG in this packet: 496, or the remainder in the last one |
| 8 | 1 |
| 9-10 | Chunk count (little-endian) |
| 11-12 | Chunk index from 0 (little-endian) |
| 13-15 | 0 |
| 16-511 | JPEG bytes, zero-padded |

- Empty packets are added until the count is a multiple of 4, so the transfer is a multiple of 2048 bytes.
- The packets are written 4096 bytes at a time, with 2048 for a final half block.
- After each frame, TRCC reads 512 bytes from the IN endpoint and doesn't check their content.
- TRCC's shared buffer limits a frame to 691,200 bytes, header included.

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
