using TRStudio.Engine;

// Lists the NVIDIA graphics cards and their PCI ids. With "galax", also does the read-only identity reads
// OpenRGB uses to tell Galax lighting controllers apart, on each card's lighting I2C port. Nothing is written.
//   GpuProbe            list cards
//   GpuProbe galax      list cards, then read Galax identity bytes
//   GpuProbe dump 50    read registers 0x00-0x4F of the chip at that I2C address (hex), on every card; read-only

var gpus = NvApiI2cBus.Gpus();
if (gpus.Count == 0)
{
    Console.WriteLine("No NVIDIA card found (no NVIDIA driver, or NvAPI wouldn't start).");
    return;
}
foreach (var gpu in gpus) Console.WriteLine($"GPU {gpu.Index}: {gpu.Ids}");
if (args.Length >= 2 && args[0] == "dump")
{
    var target = Convert.ToByte(args[1], 16);
    foreach (var gpu in gpus)
    {
        using var bus = new NvApiI2cBus(gpu);
        Console.WriteLine($"\n{bus.Name}, chip 0x{target:X2}");
        for (var row = 0; row < 0x50; row += 16)
            Console.WriteLine($"  {row:X2}: " + string.Join(" ", Enumerable.Range(row, 16).Select(r => bus.ReadByteData(target, (byte)r) is var v && v < 0 ? "--" : v.ToString("X2"))));
    }
    return;
}
if (args.Length >= 2 && args[0] == "galaxcolor")
{
    // Writes one solid color to the Galax RTX 4070 EX Gamer only (10DE:2786 10DE:1875, chip 0x50, identity 02 01),
    // in the Galax v2 layout: color at 0x02-0x04, then mode 0x30 = static. Nothing is saved to the card.
    var rgb = Convert.FromHexString(args[1]);
    var card = gpus.FirstOrDefault(g => g.Ids == "10DE:2786 10DE:1875");
    if (card == null) { Console.WriteLine("The Galax RTX 4070 EX Gamer isn't here; nothing written."); return; }
    using var bus = new NvApiI2cBus(card);
    if (bus.ReadByteData(0x50, 0x00) != 0x02 || bus.ReadByteData(0x50, 0x01) != 0x01) { Console.WriteLine("Identity check failed; nothing written."); return; }
    var ok = bus.WriteByteData(0x50, 0x02, rgb[0]) & bus.WriteByteData(0x50, 0x03, rgb[1]) & bus.WriteByteData(0x50, 0x04, rgb[2]) & bus.WriteByteData(0x50, 0x30, 0x01);
    Console.WriteLine($"Writes {(ok ? "accepted" : "FAILED")}. Read back: color {bus.ReadByteData(0x50, 0x02):X2} {bus.ReadByteData(0x50, 0x03):X2} {bus.ReadByteData(0x50, 0x04):X2}, mode {bus.ReadByteData(0x50, 0x30):X2}");
    return;
}
if (!args.Contains("galax", StringComparer.OrdinalIgnoreCase)) return;

(byte Address, byte[] Registers)[] reads = [(0x23, [0x00]), (0x32, [0x00, 0x01]), (0x50, [0x00, 0x01]), (0x51, [0x00])];
foreach (var gpu in gpus)
{
    using var bus = new NvApiI2cBus(gpu);
    Console.WriteLine($"\n{bus.Name}");
    foreach (var (address, registers) in reads)
    {
        var values = registers.Select(register => bus.ReadByteData(address, register)).ToArray();
        var shown = string.Join(" ", values.Select((value, i) => $"reg {registers[i]:X2} = {(value < 0 ? "--" : value.ToString("X2"))}"));
        Console.WriteLine($"  0x{address:X2}: {shown}{(Verdict(address, values) is { } v ? $"  <- {v}" : "")}");
    }
}

// OpenRGB GalaxGPUControllerDetect.cpp: which answer means which controller.
static string? Verdict(byte address, int[] values) => address switch
{
    0x23 when values[0] is 0x27 or 0x30 => "Galax v1 controller (0x23)",
    0x32 when values[0] is 0x27 or 0x26 && values[1] is 0x10 or 0x20 => "Galax v1 controller (0x32)",
    0x51 when values[0] == 0x80 => "Galax v2 controller (0x51, as on the 5070 Ti EX Gamer)",
    0x50 when values[0] == 0x01 && values[1] == 0x08 => "Galax v2 controller (0x50, as on the 4080 SG)",
    _ => null,
};
