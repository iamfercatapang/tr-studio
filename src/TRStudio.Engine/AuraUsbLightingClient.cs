// SPDX-License-Identifier: GPL-2.0-or-later
//
// Driver for ASUS Aura USB motherboard lighting controllers.
//
// Ported to C# from the OpenRGB project (https://gitlab.com/CalcProgrammer1/OpenRGB), files
// Controllers/AsusAuraUSBController/AsusAuraUSBController/AsusAuraUSBController.cpp,
// AsusAuraMainboardController.cpp and RGBController_AsusAuraUSB.cpp, by Adam Honse (CalcProgrammer1),
// Martin Hartl (inlart) and the OpenRGB contributors, licensed GPL-2.0-or-later. The packet layouts,
// the config table offsets and the effect list are theirs.

using System.ComponentModel;
using System.Text;
using Microsoft.Win32;

namespace TRStudio.Engine;

/// <summary>
/// One ASUS Aura USB controller: the board's own LEDs and 12V RGB headers as one fixed channel, and each
/// addressable header as a channel of its own. Every packet is 65 bytes and starts with report id 0xEC.
/// </summary>
public sealed class AuraBoard : IDisposable
{
    public const ushort Vendor = 0x0B05;
    /// <summary>Product ids of the motherboard controllers that speak this protocol.</summary>
    public static readonly ushort[] Products = [0x18F3, 0x1939, 0x19AF, 0x1AA6, 0x1BED];
    public const int MaxHeaderLeds = 500;
    public const string FixedZone = "Aura Mainboard";

    const int PacketSize = 65, LedsPerPacket = 20;
    const byte ReportId = 0xEC, RequestFirmware = 0x82, FirmwareReply = 0x02, RequestConfig = 0xB0, ConfigReply = 0x30;
    const byte Direct = 0x40, DirectApply = 0x80, DirectOverflow = 0x10, FixedDirectChannel = 0x04;
    const byte Effect = 0x35, EffectColor = 0x36;
    const byte ModeDirect = 0xFF;
    const int ConfigHeaders = 0x02, ConfigBoardLeds = 0x1B, ConfigRgbHeaders = 0x1D;
    /// <summary>This firmware numbers its addressable headers' effects from 0, with the board's own LEDs after them.</summary>
    const string HeadersFirstFirmware = "AULA3-AR32";

    /// <summary>The board's built-in effects by the number its firmware knows them by. Colors is how many each takes.</summary>
    static readonly (string Name, byte Value, int Colors)[] Effects =
    [
        ("Direct", ModeDirect, 0), ("Off", 0, 0), ("Static", 1, 1), ("Breathing", 2, 1), ("Flashing", 3, 1),
        ("Spectrum Cycle", 4, 0), ("Rainbow", 5, 0), ("Chase Fade", 7, 1), ("Chase", 9, 1),
        ("Spectrum Cycle Breathing", 6, 0), ("Spectrum Cycle Chase Fade", 8, 0), ("Spectrum Cycle Chase", 10, 0),
        ("Spectrum Cycle Wave", 11, 0), ("Chase Rainbow Pulse", 12, 0), ("Random Flicker", 13, 0),
        ("Gentle Transition", 16, 0), ("Wave Propagation", 17, 0), ("Wave Propagation Pause", 18, 0), ("Red Pulse", 19, 0),
    ];

    /// <summary>Slots is how many LEDs the firmware counts for the channel's effects; Leds is how many get colors.</summary>
    sealed record Channel(byte EffectChannel, byte DirectChannel, int Slots, bool Addressable, string Name)
    {
        public int Leds { get; set; }
    }

    readonly IHidLink _link;
    readonly List<Channel> _channels = [];

    /// <summary>Asks the controller what it has. Throws <see cref="InvalidDataException"/> when it doesn't answer as an Aura board.</summary>
    public AuraBoard(IHidLink link, string name, string key)
    {
        _link = link;
        Name = name;
        Key = key;
        Firmware = Ask(RequestFirmware, FirmwareReply) is { } firmware ? Encoding.ASCII.GetString(firmware, 2, 16).TrimEnd('\0', ' ') : "";
        var config = Ask(RequestConfig, ConfigReply) ?? throw new InvalidDataException("The Aura controller didn't send its configuration.");
        var table = config.AsSpan(4);
        int boardLeds = table[ConfigBoardLeds], headers = table[ConfigHeaders];

        byte effectChannel = 0;
        if (boardLeds > 0)
        {
            var headersFirst = Firmware.StartsWith(HeadersFirstFirmware, StringComparison.Ordinal);
            _channels.Add(new Channel(headersFirst ? (byte)headers : effectChannel, FixedDirectChannel, boardLeds, false, FixedZone) { Leds = boardLeds });
            if (!headersFirst) effectChannel++;
        }
        for (var header = 0; header < headers; header++)
            _channels.Add(new Channel(effectChannel++, (byte)header, 1, true, $"Addressable RGB Header {header + 1}"));

        Send(0x52, 0x53, 0x00, 0x01); // takes the headers out of the newer "Gen 2" strip mode, which this protocol doesn't drive
    }

    public string Name { get; }
    public string Key { get; }
    public string Firmware { get; }

    public LightDevice Describe(int index) => new(index, Key, Name, "Motherboard",
        _channels.Select(channel => new LightZone(channel.Name, channel.Leds, channel.Addressable ? 0 : channel.Leds, channel.Addressable ? MaxHeaderLeds : channel.Leds)).ToList(),
        Effects.Select(effect => new LightMode(effect.Name, false, effect.Colors)).ToList(),
        _channels.Sum(channel => channel.Leds));

    public void ResizeZone(int zone, int leds)
    {
        if (zone >= 0 && zone < _channels.Count && _channels[zone].Addressable) _channels[zone].Leds = Math.Clamp(leds, 0, MaxHeaderLeds);
    }

    public void SetDirect()
    {
        foreach (var channel in _channels.Where(channel => channel.Leds > 0)) Send(Effect, channel.EffectChannel, 0x00, 0x00, ModeDirect);
    }

    /// <summary>Colors for every LED, zone after zone.</summary>
    public void SetLeds(ReadOnlySpan<Rgb> colors)
    {
        var start = 0;
        foreach (var channel in _channels)
        {
            if (channel.Leds == 0) continue;
            if (start + channel.Leds > colors.Length) break;
            SendDirect(channel.DirectChannel, colors.Slice(start, channel.Leds));
            start += channel.Leds;
        }
    }

    /// <summary>Starts a built-in effect on every channel that has LEDs. An unknown name does nothing.</summary>
    public void SetMode(string mode, Rgb color)
    {
        var found = Array.FindIndex(Effects, effect => string.Equals(effect.Name, mode, StringComparison.OrdinalIgnoreCase));
        if (found < 0) return;
        var value = Effects[found].Value;
        var firstSlot = 0;
        foreach (var channel in _channels)
        {
            if (channel.Leds > 0)
            {
                Send(Effect, channel.EffectChannel, 0x00, 0x00, value);
                if (value != ModeDirect) SendEffectColor(firstSlot, channel.Slots, color);
            }
            firstSlot += channel.Slots;
        }
    }

    /// <summary>The effect's color goes to a run of the firmware's LED slots, picked by a bit for each.</summary>
    void SendEffectColor(int firstSlot, int slots, Rgb color)
    {
        if (firstSlot + slots > (PacketSize - 5) / 3) return; // more than one packet holds; no known board has that many
        var packet = new byte[PacketSize];
        var mask = ((1 << slots) - 1) << firstSlot;
        packet[0] = ReportId;
        packet[1] = EffectColor;
        packet[2] = (byte)(mask >> 8);
        packet[3] = (byte)mask;
        for (var slot = firstSlot; slot < firstSlot + slots; slot++)
        {
            packet[5 + slot * 3] = color.R;
            packet[6 + slot * 3] = color.G;
            packet[7 + slot * 3] = color.B;
        }
        _link.Write(packet);
    }

    /// <summary>Twenty LEDs to a packet. The last packet carries the flag that makes the strip show them.</summary>
    void SendDirect(byte channel, ReadOnlySpan<Rgb> colors)
    {
        var command = channel;
        byte offset = 0;
        var done = 0;
        while (done < colors.Length)
        {
            var count = LedsPerPacket;
            if (done + count < colors.Length) done += count;
            else
            {
                command |= DirectApply;
                count = colors.Length - done;
                done = colors.Length;
            }

            var packet = new byte[PacketSize];
            packet[0] = ReportId;
            packet[1] = Direct;
            packet[2] = command;
            packet[3] = offset;
            packet[4] = (byte)count;
            for (var i = 0; i < count; i++)
            {
                var color = colors[done - count + i];
                packet[5 + i * 3] = color.R;
                packet[6 + i * 3] = color.G;
                packet[7 + i * 3] = color.B;
            }
            _link.Write(packet);

            offset = (byte)(offset + count);
            if (done > 255)
            {
                command |= DirectOverflow; // the offset is one byte, so past 256 LEDs a flag stands for the ninth bit
                offset = (byte)(done - 256);
            }
        }
    }

    void Send(params byte[] bytes)
    {
        var packet = new byte[PacketSize];
        packet[0] = ReportId;
        bytes.CopyTo(packet, 1);
        _link.Write(packet);
    }

    byte[]? Ask(byte request, byte reply)
    {
        (_link as HidDevice)?.Flush();
        Send(request);
        // Something else's answer can be waiting first, so a few are read before giving up.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (_link.Read() is not { Length: >= PacketSize } answer) return null;
            if (answer[1] == reply) return answer;
        }
        return null;
    }

    public void Dispose() => _link.Dispose();
}

/// <summary>
/// Drives the ASUS Aura USB controllers found on this PC, with no other program running. A PC without one
/// gives a client with no devices, which is not a failure.
/// </summary>
public sealed class AuraUsbLightingClient : ILightingClient
{
    readonly List<AuraBoard> _boards;

    public AuraUsbLightingClient(IEnumerable<AuraBoard> boards) => _boards = boards.ToList();

    public static AuraUsbLightingClient Connect(Action<string>? log = null)
    {
        var boards = new List<AuraBoard>();
        var name = $"ASUS {BoardName()}".Trim();
        foreach (var product in AuraBoard.Products)
        {
            foreach (var path in HidDevice.Find(AuraBoard.Vendor, product))
            {
                var device = HidDevice.Open(path);
                if (device == null) continue;
                try
                {
                    if (device.OutputLength != 65) { device.Dispose(); continue; }
                    var key = $"{name}|AuraUSB {product:X4}{(boards.Count > 0 ? $" {boards.Count + 1}" : "")}";
                    var board = new AuraBoard(device, name, key);
                    boards.Add(board);
                    log?.Invoke($"Aura USB controller {product:X4} found, firmware {board.Firmware}, {board.Describe(0).Zones.Count} zone(s)");
                }
                catch (Exception e) when (e is IOException or InvalidDataException or Win32Exception)
                {
                    device.Dispose();
                    log?.Invoke($"Aura USB controller {product:X4} didn't answer: {e.Message}");
                }
            }
        }
        return new AuraUsbLightingClient(boards);
    }

    /// <summary>The motherboard's model as its BIOS names it.</summary>
    internal static string BoardName()
    {
        try
        {
            using var bios = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            return bios?.GetValue("BaseBoardProduct") as string ?? "Motherboard";
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return "Motherboard"; }
    }

    void Guard(int device, Action<AuraBoard> call)
    {
        if (device < 0 || device >= _boards.Count) return;
        try { call(_boards[device]); }
        catch (Exception e) when (e is IOException or Win32Exception) { throw new LightingUnavailableException($"The Aura controller stopped answering: {e.Message}", e); }
    }

    public bool TakeDevicesChanged() => false;

    public IReadOnlyList<LightDevice> Devices() => _boards.Select((board, index) => board.Describe(index)).ToList();

    public void SetDirect(int device) => Guard(device, board => board.SetDirect());

    public void SetLeds(int device, ReadOnlySpan<Rgb> colors)
    {
        var leds = colors.ToArray();
        Guard(device, board => board.SetLeds(leds));
    }

    public void SetMode(int device, string mode, int speed, IReadOnlyList<Rgb> colors) =>
        Guard(device, board => board.SetMode(mode, colors.Count > 0 ? colors[0] : Rgb.Black));

    public void ResizeZone(int device, int zone, int leds) => Guard(device, board => board.ResizeZone(zone, leds));

    public void Dispose()
    {
        foreach (var board in _boards) board.Dispose();
    }
}
