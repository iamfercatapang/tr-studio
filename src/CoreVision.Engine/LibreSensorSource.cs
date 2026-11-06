using LibreHardwareMonitor.Hardware;

namespace CoreVision.Engine;

public sealed class LibreSensorSource : ISensorSource
{
    // "Temperature #1" on boards that only number their sensors (ASUS PRIME B550M-A WIFI II); MSI calls it "System".
    static readonly string[] BoardTemperatures = ["Temperature #1", "Motherboard", "System"];

    readonly Computer _computer;
    readonly Func<FanChoice> _fans;
    readonly double? _memorySpeed = SystemSpecs.ReadMemorySpeed(); // fixed at boot, so it's read once

    /// <param name="fans">Read on every update, so a new pick in Settings shows at once.</param>
    public LibreSensorSource(Func<FanChoice>? fans = null)
    {
        _fans = fans ?? (() => new FanChoice());
        _computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true, IsMotherboardEnabled = true };
        _computer.Open();
    }

    public RawStats Read()
    {
        foreach (var hardware in _computer.Hardware)
        {
            hardware.Update();
            foreach (var sub in hardware.SubHardware) sub.Update();
        }

        var cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        var gpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel);
        var memory = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Memory && h.Name == "Total Memory");
        // Some boards have more than one sensor chip, so every one is searched.
        var boardSensors = _computer.Hardware.Where(h => h.HardwareType == HardwareType.Motherboard)
            .SelectMany(h => h.Sensors.Concat(h.SubHardware.SelectMany(sub => sub.Sensors))).ToList();
        var fans = boardSensors.Where(s => s.SensorType == SensorType.Fan)
            .Select(s => new FanHeader(s.Identifier.ToString(), s.Name, s.Value)).ToList();
        var choice = _fans();
        double? Fan(FanRole role) => FanMatch.Resolve(fans, choice, role)?.Rpm;

        return new RawStats(
            CpuTemp: Find(cpu, SensorType.Temperature, "Core (Tctl/Tdie)") ?? Find(cpu, SensorType.Temperature, "CPU Package"),
            CpuLoad: Find(cpu, SensorType.Load, "CPU Total"),
            CpuPower: Find(cpu, SensorType.Power, "Package"),
            GpuTemp: Find(gpu, SensorType.Temperature, "GPU Core"),
            GpuLoad: Find(gpu, SensorType.Load, "GPU Core"),
            GpuPower: Find(gpu, SensorType.Power, "GPU Package"),
            MemoryLoad: Find(memory, SensorType.Load, "Memory"),
            CpuClock: Find(cpu, SensorType.Clock, "Cores (Average)"),
            GpuClock: Find(gpu, SensorType.Clock, "GPU Core"),
            GpuFan: Find(gpu, SensorType.Fan, "GPU Fan 1"),
            GpuHotSpot: Find(gpu, SensorType.Temperature, "GPU Hot Spot"),
            GpuMemoryTemp: Find(gpu, SensorType.Temperature, "GPU Memory Junction"),
            MotherboardTemp: BoardTemperatures.Select(name => boardSensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature && s.Name == name))
                .FirstOrDefault(s => s != null)?.Value,
            PumpRpm: Fan(FanRole.Pump),
            CpuFanRpm: Fan(FanRole.CpuFan),
            CaseFanRpm: Fan(FanRole.CaseFan),
            MemoryUsed: Find(memory, SensorType.Data, "Memory Used"),
            MemoryAvailable: Find(memory, SensorType.Data, "Memory Available"),
            MemorySpeed: _memorySpeed,
            GpuMemoryUsed: Find(gpu, SensorType.SmallData, "GPU Memory Used"))
        {
            Fans = fans,
        };
    }

    static double? Find(IHardware? hardware, SensorType type, string name) =>
        hardware?.Sensors.FirstOrDefault(s => s.SensorType == type && s.Name == name)?.Value;

    public void Dispose() => _computer.Close();
}
