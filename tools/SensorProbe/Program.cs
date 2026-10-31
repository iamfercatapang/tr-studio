using LibreHardwareMonitor.Hardware;

// Lists every sensor LibreHardwareMonitor can read on this PC, twice, to show which values update.
var computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true, IsMotherboardEnabled = true };
computer.Open();
Update();
Thread.Sleep(1500);
Update();

foreach (var hardware in computer.Hardware) Print(hardware, "");
computer.Close();

void Update()
{
    foreach (var hardware in computer.Hardware)
    {
        hardware.Update();
        foreach (var sub in hardware.SubHardware) sub.Update();
    }
}

static void Print(IHardware hardware, string indent)
{
    Console.WriteLine($"{indent}{hardware.HardwareType}: {hardware.Name}");
    foreach (var sensor in hardware.Sensors.Where(s => s.SensorType is SensorType.Temperature or SensorType.Load or SensorType.Power or SensorType.Fan or SensorType.Clock))
        Console.WriteLine($"{indent}  {sensor.SensorType,-12} {sensor.Name,-32} {(sensor.Value?.ToString("F1") ?? "null")}");
    foreach (var sub in hardware.SubHardware) Print(sub, indent + "  ");
}
