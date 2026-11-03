using LibreHardwareMonitor.Hardware;

namespace CoreVision.Engine;

public sealed class LibreSensorSource : ISensorSource
{
    // On the ASUS PRIME B550M-A WIFI II. The pump speed confirms Fan #7; the others are best matches for that board.
    const string PumpHeader = "Fan #7", CpuFanHeader = "Fan #2", CaseFanHeader = "Fan #3";
    const string BoardTemperature = "Temperature #1";

    readonly Computer _computer;
    readonly double? _memorySpeed = SystemSpecs.ReadMemorySpeed(); // fixed at boot, so it's read once

    public LibreSensorSource()
    {
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
        var board = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Motherboard)?.SubHardware.FirstOrDefault();

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
            MotherboardTemp: Find(board, SensorType.Temperature, BoardTemperature),
            PumpRpm: Find(board, SensorType.Fan, PumpHeader),
            CpuFanRpm: Find(board, SensorType.Fan, CpuFanHeader),
            CaseFanRpm: Find(board, SensorType.Fan, CaseFanHeader),
            MemoryUsed: Find(memory, SensorType.Data, "Memory Used"),
            MemoryAvailable: Find(memory, SensorType.Data, "Memory Available"),
            MemorySpeed: _memorySpeed,
            GpuMemoryUsed: Find(gpu, SensorType.SmallData, "GPU Memory Used"));
    }

    static double? Find(IHardware? hardware, SensorType type, string name) =>
        hardware?.Sensors.FirstOrDefault(s => s.SensorType == type && s.Name == name)?.Value;

    public void Dispose() => _computer.Close();
}
