using TRStudio.Engine;
using Xunit;

public class GpuPickTests
{
    static GpuCandidate Amd(string name, double? memory = null) => new(name, GpuMaker.Amd, memory);
    static GpuCandidate Intel(string name, double? memory = null) => new(name, GpuMaker.Intel, memory);
    static GpuCandidate Nvidia(string name, double? memory = null) => new(name, GpuMaker.Nvidia, memory);

    [Fact]
    public void Ryzen_graphics_listed_first_loses_to_the_radeon_card()
    {
        // The reported PC: Ryzen 7 9800X3D with an RX 9070 XT.
        var gpus = new[] { Amd("AMD Radeon(TM) Graphics", 512), Amd("AMD Radeon RX 9070 XT", 16304) };
        Assert.Equal(1, GpuPick.Choose(gpus));
    }

    [Fact]
    public void Built_in_graphics_lose_even_when_memory_is_unknown()
    {
        Assert.Equal(1, GpuPick.Choose([Amd("AMD Radeon Graphics"), Amd("AMD Radeon RX 7800 XT")]));
        Assert.Equal(1, GpuPick.Choose([Intel("Intel(R) UHD Graphics 770"), Nvidia("NVIDIA GeForce RTX 4070")]));
        Assert.Equal(1, GpuPick.Choose([Intel("Intel(R) Arc(TM) Graphics"), Nvidia("NVIDIA GeForce RTX 4060 Laptop GPU")]));
    }

    [Fact]
    public void A_lone_built_in_gpu_is_still_read()
    {
        Assert.Equal(0, GpuPick.Choose([Amd("AMD Radeon(TM) Graphics", 2048)]));
    }

    [Fact]
    public void Two_cards_pick_the_one_with_more_memory()
    {
        Assert.Equal(1, GpuPick.Choose([Nvidia("NVIDIA GeForce GTX 1650", 4096), Nvidia("NVIDIA GeForce RTX 4090", 24564)]));
    }

    [Fact]
    public void Ties_keep_the_first_card()
    {
        Assert.Equal(0, GpuPick.Choose([Nvidia("NVIDIA GeForce RTX 3080"), Amd("AMD Radeon RX 6800")]));
        Assert.Equal(-1, GpuPick.Choose([]));
    }

    [Fact]
    public void The_gpu_list_survives_stale_readings()
    {
        // Settings keeps showing the picker while the sensors catch up after sleep.
        var gpus = new[] { new GpuInfo("/gpu-amd/0", "AMD Radeon(TM) Graphics", 512, false), new GpuInfo("/gpu-amd/1", "AMD Radeon RX 9070 XT", 16304, true) };
        var stats = Stats.From(new RawStats { Gpus = gpus });
        Assert.Equal(gpus, stats.Gpus);
        Assert.Equal(gpus, stats.AsStale().Gpus);
    }

    [Fact]
    public void Discrete_intel_arc_is_not_mistaken_for_built_in()
    {
        Assert.False(GpuPick.LooksIntegrated(Intel("Intel(R) Arc(TM) B580 Graphics")));
        Assert.False(GpuPick.LooksIntegrated(Intel("Intel(R) Arc(TM) A770 Graphics")));
        Assert.True(GpuPick.LooksIntegrated(Intel("Intel(R) Iris(R) Xe Graphics")));
    }
}
