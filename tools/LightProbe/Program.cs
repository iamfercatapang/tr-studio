// Lists the lighting devices TR Studio can drive on this PC, then runs a canvas effect on every one
// through the same code the app uses. ASUS Aura USB boards are driven directly; anything else shows
// up only while OpenRGB is open with its SDK server started.
//   LightProbe                 list devices, then 10 seconds of Rainbow Wave
//   LightProbe aurora 20       another effect by its id (solid, rainbow, sweep, radar...), for 20 seconds
//   add columns to stand every strip upright, side by side, instead of in rows: a sweep then crosses them one after another
//   LightProbe list            list only
//   add header=30 to say how many LEDs are on each addressable header, such as: LightProbe Comet 10 header=30
//   add asrock to include an ASRock Polychrome USB board: LightProbe list asrock
//   add gpu to include a supported graphics card (GALAX and others in GpuCatalog): LightProbe solid 5 gpu
//   add ram to include RGB memory, which needs a terminal opened as administrator: LightProbe list ram
using System.Diagnostics;
using TRStudio.Engine;

if (args.Length > 0 && args[0].Equals("ramcheck", StringComparison.OrdinalIgnoreCase))
{
    // Sets every stick to one solid color and reads the chip back, to see whether writes arrive. Needs administrator.
    var color = args.Length > 1 ? Rgb.Parse(args[1], new Rgb(255, 0, 0)) : new Rgb(255, 0, 0);
    using var bus = PawnIoSmBus.Open(0, Console.WriteLine);
    if (bus == null) return 1;
    foreach (var stick in EneRamLightingClient.Find(bus, Console.WriteLine))
    {
        Console.WriteLine($"0x{stick.Address:X2} before: {stick.ReadState()}");
        stick.SetDirect();
        var leds = new Rgb[stick.LedCount];
        Array.Fill(leds, color);
        stick.SetLeds(leds);
        Console.WriteLine($"0x{stick.Address:X2} after:  {stick.ReadState()}");
    }
    Console.WriteLine($"Every stick should now be solid {color.ToHex()}.");
    return 0;
}

var withMemory = args.Any(arg => arg.Equals("ram", StringComparison.OrdinalIgnoreCase));
var withAsrock = args.Any(arg => arg.Equals("asrock", StringComparison.OrdinalIgnoreCase));
var withGpu = args.Any(arg => arg.Equals("gpu", StringComparison.OrdinalIgnoreCase));
var columns = args.Any(arg => arg.Equals("columns", StringComparison.OrdinalIgnoreCase));
string[] flags = ["ram", "asrock", "gpu", "columns"];
var options = args.Where(arg => !arg.Contains('=') && !flags.Contains(arg, StringComparer.OrdinalIgnoreCase)).ToArray();
var headerLeds = args.Select(arg => arg.Split('=')).Where(pair => pair.Length == 2 && pair[0].Equals("header", StringComparison.OrdinalIgnoreCase))
    .Select(pair => int.TryParse(pair[1], out var count) ? count : 0).FirstOrDefault();
var listOnly = options.Length > 0 && options[0].Equals("list", StringComparison.OrdinalIgnoreCase);
var effect = CanvasEffects.Find(options.Length > 0 ? options[0].ToLowerInvariant() : "rainbow");
var seconds = options.Length > 1 && int.TryParse(options[1], out var given) ? Math.Clamp(given, 1, 600) : 10;

EneRamLightingClient? memory = null;
CombinedLightingClient? latest = null;
ILightingClient Connect() => latest = new CombinedLightingClient(new BuiltInLightingClient(AuraUsbLightingClient.Connect(Console.WriteLine),
    withAsrock ? AsrockUsbLightingClient.Connect(Console.WriteLine) : new AsrockUsbLightingClient([]),
    memory = withMemory ? EneRamLightingClient.Connect(Console.WriteLine) : new EneRamLightingClient(null, []),
    withGpu ? GpuLightingClient.Connect(Console.WriteLine) : new GpuLightingClient([])), OpenRgbLightingClient.Connect);

IReadOnlyList<LightDevice> devices;
using (var client = Connect())
{
    client.TakeDevicesChanged(); // starts looking for OpenRGB
    Thread.Sleep(2500);
    client.TakeDevicesChanged();
    devices = client.Devices();
    Console.WriteLine(latest!.ExtraConnected ? "OpenRGB is connected" : "OpenRGB isn't answering, so only built-in devices are listed");
}

Console.WriteLine($"{devices.Count} device(s)");
foreach (var device in devices)
{
    Console.WriteLine($"[{device.Index}] {device.Name} ({device.Kind}), {device.LedCount} LEDs, key \"{device.Key}\"");
    foreach (var zone in device.Zones)
        Console.WriteLine($"      zone {zone.Name}: {zone.LedCount} LEDs{(zone.Resizable ? $", resizable {zone.MinLeds} to {zone.MaxLeds}" : "")}");
    Console.WriteLine($"      modes: {string.Join(", ", device.Modes.Select(mode => mode.Name + (mode.HasSpeed ? " (speed)" : "") + (mode.Colors > 0 ? $" ({mode.Colors} colors)" : "")))}");
}
if (listOnly || devices.Count == 0) return 0;

var canvas = new CanvasSettings(effect.Id);
if (columns)
{
    // Each strip stands upright in its own column, left to right in the order listed.
    var zones = devices.SelectMany(device => device.Zones.Select(zone => LightingSettings.HeaderKey(device.Key, zone.Name))).ToList();
    var step = LightPlacement.CanvasWidth / zones.Count;
    for (var i = 0; i < zones.Count; i++) canvas = canvas.WithPlacement(zones[i], new LightPlacement(i * step + step * 0.2, 20, step * 0.6, 160));
}
var settings = new LightingSettings(Enabled: true, Canvas: canvas);
if (headerLeds > 0)
{
    foreach (var device in devices)
        foreach (var zone in device.Zones.Where(zone => zone.Resizable))
            settings = settings.WithHeaderLeds(device.Key, zone.Name, headerLeds);
}
Console.WriteLine($"Running {effect.Name} for {seconds} s{(columns ? ", strips side by side" : "")}. The lights stay on the last frame afterwards.");
using var service = new LightingService(Connect, settings, Console.WriteLine);
var clock = Stopwatch.StartNew();
var frames = 0;
while (clock.Elapsed.TotalSeconds < seconds)
{
    if (service.Step(clock.Elapsed.TotalSeconds)) frames++;
    Thread.Sleep(1000 / LightingService.FramesPerSecond);
}
Console.WriteLine($"{frames} frames, {frames / clock.Elapsed.TotalSeconds:0.0} a second, state {service.State}, {service.Devices.Sum(device => device.LedCount)} LEDs lit");
if (withMemory) Console.WriteLine($"Memory: {memory?.FramesWritten} stick frames written, {memory?.FramesWritten / clock.Elapsed.TotalSeconds / Math.Max(1, devices.Count(device => device.Kind == "Dram")):0.0} a second on each stick");
return service.State == LightingState.Connected ? 0 : 1;
