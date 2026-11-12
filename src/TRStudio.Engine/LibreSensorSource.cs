using LibreHardwareMonitor.Hardware;

namespace TRStudio.Engine;

public sealed class LibreSensorSource : ISensorSource
{
    // "Temperature #1" on boards that only number their sensors (ASUS PRIME B550M-A WIFI II); MSI calls it "System".
    static readonly string[] BoardTemperatures = ["Temperature #1", "Motherboard", "System"];

    readonly Computer _computer;
    readonly Func<FanChoice> _fans;
    readonly double? _memorySpeed = SystemSpecs.ReadMemorySpeed(); // fixed at boot, so it's read once
    readonly Func<string?> _gpuChoice;
    readonly Action<string> _log;
    List<IHardware> _gpus = [];
    IHardware? _automaticGpu;
    IHardware? _readGpu;

    /// <param name="fans">Read on every update, so a new pick in Settings shows at once.</param>
    /// <param name="gpu">The GPU picked in Settings, by identifier; null or a GPU that's gone means automatic. Read on every update.</param>
    public LibreSensorSource(Func<FanChoice>? fans = null, Action<string>? log = null, Func<string?>? gpu = null)
    {
        _fans = fans ?? (() => new FanChoice());
        _gpuChoice = gpu ?? (() => null);
        _log = log ?? (_ => { });
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
        var gpu = ChooseGpu();
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
            // NVIDIA and Intel say "GPU Package"; AMD reports "GPU PPT" or "GPU Power" depending on the card.
            GpuPower: Find(gpu, SensorType.Power, "GPU Package", "GPU PPT", "GPU Power", "GPU Core"),
            MemoryLoad: Find(memory, SensorType.Load, "Memory"),
            CpuClock: Find(cpu, SensorType.Clock, "Cores (Average)"),
            GpuClock: Find(gpu, SensorType.Clock, "GPU Core"),
            GpuFan: Find(gpu, SensorType.Fan, "GPU Fan 1", "GPU Fan"), // NVIDIA numbers its fans, AMD doesn't
            GpuHotSpot: Find(gpu, SensorType.Temperature, "GPU Hot Spot"),
            GpuMemoryTemp: Find(gpu, SensorType.Temperature, "GPU Memory Junction", "GPU Memory"), // NVIDIA, AMD
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
            Gpus = _gpus.Select(h => new GpuInfo(h.Identifier.ToString(), h.Name, MemoryTotal(h), h == _automaticGpu)).ToList(),
        };
    }

    /// <summary>The first of <paramref name="names"/> that has a reading, as cards from different makers name the same sensor differently.</summary>
    static double? Find(IHardware? hardware, SensorType type, params string[] names) =>
        names.Select(name => hardware?.Sensors.FirstOrDefault(s => s.SensorType == type && s.Name == name)?.Value)
            .FirstOrDefault(value => value != null);

    static GpuMaker? Maker(IHardware hardware) => hardware.HardwareType switch
    {
        HardwareType.GpuNvidia => GpuMaker.Nvidia,
        HardwareType.GpuAmd => GpuMaker.Amd,
        HardwareType.GpuIntel => GpuMaker.Intel,
        _ => null,
    };

    static double? MemoryTotal(IHardware gpu) => Find(gpu, SensorType.SmallData, "GPU Memory Total");

    /// <summary>
    /// The GPU picked in Settings, else the automatic pick. The automatic pick is made again only when the list
    /// of GPUs changes, so it doesn't flicker between ticks.
    /// </summary>
    IHardware? ChooseGpu()
    {
        var gpus = _computer.Hardware.Where(h => Maker(h) != null).ToList();
        if (!gpus.SequenceEqual(_gpus))
        {
            var chosen = GpuPick.Choose(gpus.Select(h => new GpuCandidate(h.Name, Maker(h)!.Value, MemoryTotal(h))).ToList());
            _automaticGpu = chosen < 0 ? null : gpus[chosen];
            _gpus = gpus;
            if (gpus.Count == 0) _log("No GPU found by the sensor library");
        }

        var picked = _gpuChoice();
        var gpu = gpus.FirstOrDefault(h => h.Identifier.ToString() == picked) ?? _automaticGpu;
        if (gpu != _readGpu)
        {
            _readGpu = gpu;
            // Logged once per change, so a bug report shows which GPU was read and what it offers.
            foreach (var h in gpus)
            {
                string Has(SensorType type, params string[] names) => Find(h, type, names) == null ? "-" : "ok";
                var memory = MemoryTotal(h);
                _log($"GPU {(h == gpu ? "[reading]" : "[skipped]")}{(h == gpu && gpu != _automaticGpu ? " (picked in Settings)" : "")} {h.HardwareType} \"{h.Name}\", " +
                     $"memory {(memory == null ? "?" : $"{memory:F0} MB")}, temp {Has(SensorType.Temperature, "GPU Core")}, load {Has(SensorType.Load, "GPU Core")}, " +
                     $"power {Has(SensorType.Power, "GPU Package", "GPU PPT", "GPU Power", "GPU Core")}");
            }
        }
        return gpu;
    }

    public void Dispose() => _computer.Close();
}
