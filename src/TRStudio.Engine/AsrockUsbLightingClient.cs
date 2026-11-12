// SPDX-License-Identifier: GPL-2.0-or-later
//
// Driver for ASRock Polychrome USB motherboard lighting controllers.
//
// Ported to C# from the OpenRGB project (https://gitlab.com/CalcProgrammer1/OpenRGB), files
// Controllers/ASRockPolychromeUSBController/ASRockPolychromeUSBController.cpp and
// RGBController_ASRockPolychromeUSB.cpp, by Ed Kambulow (dredvard), Shady Nawara (ShadyNawara) and the
// OpenRGB contributors, licensed GPL-2.0-or-later. The packet layouts, the zone list and the effect
// list are theirs.

using System.ComponentModel;

namespace TRStudio.Engine;

/// <summary>
/// One ASRock Polychrome USB controller. It has up to eight zones: two 12V RGB headers, up to three
/// addressable headers, and the board's own lights. The board itself remembers how many LEDs each
/// addressable header has. Every packet is 64 bytes after a zero report id, and the board answers each one.
/// </summary>
public sealed class AsrockBoard : IDisposable
{
    public const ushort Vendor = 0x26CE;
    public static readonly ushort[] Products = [0x01A2];
    public const int MaxHeaderLeds = 100;

    const int PacketSize = 65, ZoneCount = 8, AckMs = 150;
    const byte SetZone = 0x10, ReadHeader = 0x14, WriteHeader = 0x15, FirstChunk = 0xE3, NextChunk = 0xE4;
    const byte ConfigZones = 0x01, ConfigLedCount = 0x02, ConfigRedGreenSwap = 0x03;
    const byte ZoneMissing = 0x1E, ModeDirect = 0x0F, DefaultSpeed = 0xE0;
    const int FirstChunkAt = 9, FirstChunkBytes = 54, NextChunkAt = 5, NextChunkBytes = 57;

    static readonly string[] ZoneNames =
        ["RGB LED 1 Header", "RGB LED 2 Header", "Addressable Header 1", "Addressable Header 2", "PCH", "IO Cover", "PCB", "Addressable Header 3/Audio"];
    /// <summary>Zones whose LED count the user sets: the two addressable headers, and the last zone, a third header on newer boards.</summary>
    static readonly int[] AddressableZones = [2, 3, 7];

    /// <summary>The board's built-in effects by number, slowest speed 0xFF and fastest 0.</summary>
    static readonly (string Name, byte Value, bool Speed, bool Color)[] Effects =
    [
        ("Off", 0x00, false, false), ("Static", 0x01, false, true), ("Breathing", 0x02, true, true), ("Strobe", 0x03, true, true),
        ("Spectrum Cycle", 0x04, true, false), ("Random", 0x05, true, false), ("Wave", 0x07, true, false), ("Spring", 0x08, true, false),
        ("Stack", 0x09, true, false), ("Cram", 0x0A, true, false), ("Scan", 0x0B, true, false), ("Neon", 0x0C, false, false),
        ("Water", 0x0D, true, false), ("Rainbow", 0x0E, true, false), ("Direct", ModeDirect, false, false),
    ];

    /// <summary>Kind is the zone's place in the board's list of eight. RedGreenSwapped: its LEDs take green before red.</summary>
    sealed record Zone(byte Kind, bool Addressable, bool RedGreenSwapped)
    {
        public int Leds { get; set; }
        public int MaxLeds => Addressable ? MaxHeaderLeds : Leds;
    }

    readonly IHidLink _link;
    readonly List<Zone> _zones = [];
    readonly byte[] _ledCounts;
    readonly byte _swapConfig;
    bool _swapCleared;

    /// <summary>Asks the board which zones it has. Throws <see cref="InvalidDataException"/> when it doesn't answer.</summary>
    public AsrockBoard(IHidLink link, string name, string key)
    {
        _link = link;
        Name = name;
        Key = key;
        _ledCounts = AskConfig(ConfigLedCount, ZoneCount);
        _swapConfig = AskConfig(ConfigRedGreenSwap, 1)[0];
        var present = AskConfig(ConfigZones, 1)[0];
        for (var kind = 0; kind < ZoneCount; kind++)
        {
            if (_ledCounts[kind] == ZoneMissing || (present >> kind & 1) == 0) continue;
            _zones.Add(new Zone((byte)kind, AddressableZones.Contains(kind), (_swapConfig >> kind & 1) == 1) { Leds = _ledCounts[kind] });
        }
    }

    public string Name { get; }
    public string Key { get; }

    /// <summary>Each zone with the LED count the board keeps for it, for the log: "Addressable Header 1 0 LEDs (swapped)".</summary>
    public string ZoneSummary => string.Join(", ", _zones.Select(zone =>
        $"{ZoneNames[zone.Kind]} {zone.Leds} LED{(zone.Leds == 1 ? "" : "s")}{(zone.RedGreenSwapped ? " (swapped)" : "")}"));

    public LightDevice Describe(int index) => new(index, Key, Name, "Motherboard",
        _zones.Select(zone => new LightZone(ZoneNames[zone.Kind], zone.Leds, zone.Addressable ? 0 : zone.Leds, zone.MaxLeds)).ToList(),
        Effects.Select(effect => new LightMode(effect.Name, effect.Speed, effect.Color ? 1 : 0)).ToList(),
        _zones.Sum(zone => zone.Leds));

    /// <summary>The board keeps the count, so it is written to the board along with every other zone's.</summary>
    public void ResizeZone(int zone, int leds)
    {
        if (zone < 0 || zone >= _zones.Count || !_zones[zone].Addressable) return;
        var count = Math.Clamp(leds, 0, MaxHeaderLeds);
        // The board uses 30 to mean "no such zone", so a header set to 30 LEDs would vanish. 31 is stored instead;
        // the one LED too many is never there to light.
        if (count == ZoneMissing) count++;
        if (count == _zones[zone].Leds) return;
        _zones[zone].Leds = count;
        _ledCounts[_zones[zone].Kind] = (byte)count;
        SendConfig(ConfigLedCount, _ledCounts);
    }

    /// <summary>
    /// Puts every zone with LEDs in the mode that takes a color for each LED. The board's red-green swap is
    /// switched off meanwhile, because it makes the lights flash on every update; it is put back when
    /// an effect starts or the link closes, since the board keeps that setting.
    /// </summary>
    public void SetDirect()
    {
        if (!_swapCleared)
        {
            SendConfig(ConfigRedGreenSwap, [0]);
            _swapCleared = true;
        }
        foreach (var zone in _zones.Where(zone => zone.Leds > 0)) SendZone(zone, ModeDirect, DefaultSpeed, Rgb.Black);
    }

    /// <summary>Starts a built-in effect on every zone that has LEDs. An unknown name does nothing.</summary>
    public void SetMode(string mode, int speed, Rgb color)
    {
        var found = Array.FindIndex(Effects, effect => string.Equals(effect.Name, mode, StringComparison.OrdinalIgnoreCase));
        if (found < 0) return;
        var effect = Effects[found];
        if (effect.Value == ModeDirect) { SetDirect(); return; }
        RestoreSwap();
        var pace = effect.Speed ? (byte)Math.Round(255 - 255 * Math.Clamp(speed, 0, 100) / 100.0) : DefaultSpeed;
        foreach (var zone in _zones.Where(zone => zone.Leds > 0)) SendZone(zone, effect.Value, pace, color);
    }

    /// <summary>
    /// Colors for every LED, zone after zone, sent as one run of bytes cut into packets: 18 LEDs in the
    /// first and 19 in each one after. The run is padded with zeros to the most LEDs the board could have.
    /// </summary>
    public void SetLeds(ReadOnlySpan<Rgb> colors)
    {
        var bytes = new byte[(_zones.Sum(zone => zone.MaxLeds) + ZoneCount) * 3];
        var at = 0;
        var led = 0;
        foreach (var zone in _zones)
        {
            for (var i = 0; i < zone.Leds && led < colors.Length; i++, led++)
            {
                var color = colors[led];
                bytes[at++] = zone.RedGreenSwapped ? color.R : color.G;
                bytes[at++] = zone.RedGreenSwapped ? color.G : color.R;
                bytes[at++] = color.B;
            }
        }

        var sent = 0;
        var first = true;
        var packets = 0;
        var answered = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (sent < bytes.Length)
        {
            var packet = new byte[PacketSize];
            packet[1] = SetZone;
            packet[3] = 0xFF;
            packet[4] = first ? FirstChunk : NextChunk;
            if (first) packet[7] = 0xFF;
            packet[0x40] = 0x65;
            var count = Math.Min(first ? FirstChunkBytes : NextChunkBytes, bytes.Length - sent);
            bytes.AsSpan(sent, count).CopyTo(packet.AsSpan(first ? FirstChunkAt : NextChunkAt));
            sent += count;
            first = false;
            _link.Write(packet);
            packets++;
            if (_link.Read(AckMs) != null) answered++;
        }
        if (!_frameLogged)
        {
            // Once per connection: whether the board answers color packets, which the log can't show otherwise.
            _frameLogged = true;
            Log?.Invoke($"Polychrome first frame: {colors.Length} LEDs in {packets} packets, {answered} answered, {clock.ElapsedMilliseconds} ms");
        }
    }

    bool _frameLogged;

    /// <summary>Where the one-time frame report goes.</summary>
    public Action<string>? Log { get; set; }

    void SendZone(Zone zone, byte mode, byte speed, Rgb color)
    {
        var packet = new byte[PacketSize];
        packet[1] = SetZone;
        packet[3] = zone.Kind;
        packet[4] = mode;
        packet[5] = zone.RedGreenSwapped ? color.R : color.G;
        packet[6] = zone.RedGreenSwapped ? color.G : color.R;
        packet[7] = color.B;
        packet[8] = speed;
        packet[9] = 0xFF;
        _link.Write(packet);
        _link.Read(AckMs);
    }

    void SendConfig(byte config, ReadOnlySpan<byte> values)
    {
        var packet = new byte[PacketSize];
        packet[1] = WriteHeader;
        packet[3] = config;
        values.CopyTo(packet.AsSpan(4));
        _link.Write(packet);
        _link.Read(AckMs);
    }

    /// <summary>The answer carries the report id first, then the values from its fifth byte on.</summary>
    byte[] AskConfig(byte config, int count)
    {
        (_link as HidDevice)?.Flush();
        var packet = new byte[PacketSize];
        packet[1] = ReadHeader;
        packet[3] = config;
        _link.Write(packet);
        if (_link.Read() is not { Length: >= 5 + ZoneCount } answer) throw new InvalidDataException("The Polychrome controller didn't send its configuration.");
        return answer[5..(5 + count)];
    }

    void RestoreSwap()
    {
        if (!_swapCleared) return;
        SendConfig(ConfigRedGreenSwap, [_swapConfig]);
        _swapCleared = false;
    }

    public void Dispose()
    {
        try { RestoreSwap(); }
        catch (Exception e) when (e is IOException or Win32Exception) { /* the board is gone; nothing to put back */ }
        _link.Dispose();
    }
}

/// <summary>
/// Drives the ASRock Polychrome USB controllers found on this PC, with no other program running. A PC
/// without one gives a client with no devices.
/// </summary>
public sealed class AsrockUsbLightingClient : ILightingClient
{
    readonly List<AsrockBoard> _boards;

    public AsrockUsbLightingClient(IEnumerable<AsrockBoard> boards) => _boards = boards.ToList();

    public static AsrockUsbLightingClient Connect(Action<string>? log = null)
    {
        var boards = new List<AsrockBoard>();
        var name = $"ASRock {AuraUsbLightingClient.BoardName()}".Trim();
        foreach (var product in AsrockBoard.Products)
        {
            foreach (var path in HidDevice.Find(AsrockBoard.Vendor, product))
            {
                var device = HidDevice.Open(path);
                if (device == null) continue;
                try
                {
                    if (device.OutputLength != 65) { device.Dispose(); continue; }
                    var board = new AsrockBoard(device, name, $"{name}|PolychromeUSB {product:X4}{(boards.Count > 0 ? $" {boards.Count + 1}" : "")}");
                    board.Log = log;
                    boards.Add(board);
                    log?.Invoke($"Polychrome USB controller {product:X4} found, {board.Describe(0).Zones.Count} zone(s): {board.ZoneSummary}");
                }
                catch (Exception e) when (e is IOException or InvalidDataException or Win32Exception)
                {
                    device.Dispose();
                    log?.Invoke($"Polychrome USB controller {product:X4} didn't answer: {e.Message}");
                }
            }
        }
        return new AsrockUsbLightingClient(boards);
    }

    void Guard(int device, Action<AsrockBoard> call)
    {
        if (device < 0 || device >= _boards.Count) return;
        try { call(_boards[device]); }
        catch (Exception e) when (e is IOException or Win32Exception) { throw new LightingUnavailableException($"The Polychrome controller stopped answering: {e.Message}", e); }
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
        Guard(device, board => board.SetMode(mode, speed, colors.Count > 0 ? colors[0] : Rgb.Black));

    public void ResizeZone(int device, int zone, int leds) => Guard(device, board => board.ResizeZone(zone, leds));

    public void Dispose()
    {
        foreach (var board in _boards) board.Dispose();
    }
}
