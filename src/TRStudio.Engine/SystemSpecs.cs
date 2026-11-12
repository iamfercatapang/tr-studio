using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace TRStudio.Engine;

public sealed record SpecRow(string Label, IReadOnlyList<string> Values);

public sealed record MemoryModule(long Bytes, int Type, int SpeedMhz, string? Maker, string? Part);

/// <summary>The PC's parts, read once from Windows. A part that can't be read is left out, never a failure.</summary>
public static partial class SystemSpecs
{
    const long Gibibyte = 1024L * 1024 * 1024;
    const int Ddr3 = 24, Ddr4 = 26, Ddr5 = 34;

    public static IReadOnlyList<SpecRow> Read(Action<string>? log = null)
    {
        IEnumerable<ManagementBaseObject> Query(string wql)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(wql);
                return searcher.Get().Cast<ManagementBaseObject>().ToList();
            }
            catch (Exception e) when (e is ManagementException or COMException or UnauthorizedAccessException)
            {
                log?.Invoke($"Could not read system details: {e.Message}");
                return [];
            }
        }
        static string? Text(ManagementBaseObject item, string name) => item[name]?.ToString()?.Trim();
        static long Number(ManagementBaseObject item, string name) => long.TryParse(Text(item, name), out var value) ? value : 0;

        var memory = Query("SELECT Capacity, SMBIOSMemoryType, ConfiguredClockSpeed, Speed, Manufacturer, PartNumber FROM Win32_PhysicalMemory")
            .Select(m =>
            {
                var speed = Number(m, "ConfiguredClockSpeed");
                return new MemoryModule(Number(m, "Capacity"), (int)Number(m, "SMBIOSMemoryType"), (int)(speed > 0 ? speed : Number(m, "Speed")), Text(m, "Manufacturer"), Text(m, "PartNumber"));
            });
        var system = Query("SELECT Caption, BuildNumber FROM Win32_OperatingSystem").FirstOrDefault();
        var displayVersion = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion", null) as string;

        (string Label, IEnumerable<string?> Values)[] rows =
        [
            ("Processor", Query("SELECT Name FROM Win32_Processor").Select(p => CleanName(Text(p, "Name")))),
            ("Graphics", Query("SELECT Name, PNPDeviceID FROM Win32_VideoController")
                .Where(g => Text(g, "PNPDeviceID")?.StartsWith(@"PCI\", StringComparison.OrdinalIgnoreCase) == true) // skips virtual displays
                .Select(g => CleanName(Text(g, "Name")))),
            ("Motherboard", Query("SELECT Manufacturer, Product FROM Win32_BaseBoard").Select(b => BoardName(Text(b, "Manufacturer"), Text(b, "Product")))),
            ("Memory", [DescribeMemory(memory.ToList())]),
            ("Storage", Query("SELECT Model, Size, InterfaceType FROM Win32_DiskDrive")
                .Where(d => Text(d, "InterfaceType") != "USB")
                .Select(d => DescribeDisk(Text(d, "Model"), Number(d, "Size")))),
            ("Operating System", [system == null ? null : DescribeSystem(Text(system, "Caption"), displayVersion, Text(system, "BuildNumber"))]),
        ];
        return rows
            .Select(row => new SpecRow(row.Label, row.Values.OfType<string>().Where(value => value.Length > 0).ToList()))
            .Where(row => row.Values.Count > 0)
            .ToList();
    }

    /// <summary>The speed the memory actually runs at, in MT/s. Null when Windows doesn't say.</summary>
    public static double? ReadMemorySpeed()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT ConfiguredClockSpeed, Speed FROM Win32_PhysicalMemory");
            var module = searcher.Get().Cast<ManagementBaseObject>().FirstOrDefault();
            return Convert.ToDouble(module?["ConfiguredClockSpeed"] ?? module?["Speed"]) is > 0 and var speed ? speed : null;
        }
        catch (Exception e) when (e is ManagementException or COMException or UnauthorizedAccessException) { return null; }
    }

    public static string CleanName(string? name) =>
        Spaces().Replace((name ?? "").Replace("(R)", "").Replace("(TM)", ""), " ").Trim();

    public static string BoardName(string? maker, string? product)
    {
        var shortMaker = Regex.Replace(maker ?? "", @"(?i)^ASUSTeK.*$", "ASUS");
        shortMaker = Regex.Replace(shortMaker, @"(?i)[,.]?\s*(Co\.?,?\s*)?(Ltd\.?|Inc\.?|Corporation|Corp\.?)$", "");
        return CleanName($"{shortMaker} {product}");
    }

    public static string? DescribeMemory(IReadOnlyList<MemoryModule> modules)
    {
        if (modules.Count == 0) return null;
        var first = modules[0];
        var type = first.Type switch { Ddr3 => "DDR3", Ddr4 => "DDR4", Ddr5 => "DDR5", _ => "" };
        var speed = first.SpeedMhz <= 0 ? "" : type.Length > 0 ? $"-{first.SpeedMhz}" : $"{first.SpeedMhz} MT/s";
        var total = $"{modules.Sum(m => m.Bytes) / Gibibyte} GB";
        var sticks = modules.Select(m => m.Bytes).Distinct().Count() == 1 ? $"{modules.Count} × {first.Bytes / Gibibyte} GB" : $"{modules.Count} modules";
        return CleanName($"{total} {type}{speed} ({sticks}) {first.Part}");
    }

    /// <summary>Drives are sold in decimal sizes, so that's what's shown.</summary>
    public static string DescribeDisk(string? model, long bytes)
    {
        var name = CleanName(model);
        if (bytes <= 0) return name;
        var size = bytes >= 999_500_000_000 ? $"{bytes / 1e12:0.#} TB" : $"{bytes / 1e9:0} GB";
        return $"{name}, {size}";
    }

    public static string DescribeSystem(string? caption, string? version, string? build)
    {
        var name = CleanName((caption ?? "").Replace("Microsoft ", ""));
        return CleanName($"{name} {version}{(string.IsNullOrEmpty(build) ? "" : $" (Build {build})")}");
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
