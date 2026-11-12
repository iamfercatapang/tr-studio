using TRStudio.Engine;
using Xunit;

public sealed class GpuCatalogTests
{
    [Fact]
    public void HoldsOpenRgbsWholeList() => Assert.True(GpuCatalog.All.Count > 900, $"only {GpuCatalog.All.Count} entries");

    [Fact]
    public void HasNoExactDuplicates()
    {
        var dupes = GpuCatalog.All.GroupBy(e => e).Where(g => g.Count() > 1).Select(g => g.Key.Name).ToList();
        Assert.Empty(dupes);
    }

    [Fact]
    public void UsersGalaxCardResolvesToGalax()
    {
        var match = Assert.Single(GpuCatalog.Find(0x10DE, 0x2786, 0x10DE, 0x1875));
        Assert.Equal("GalaxGPUController", match.Driver);
        Assert.Equal(0x50, match.Address);
    }

    [Fact]
    public void OpenRgbEntryKeepsItsAddress()
    {
        var match = Assert.Single(GpuCatalog.All, e => e.Name == "KFA2 GeForce RTX 4080 SG 1-Click OC");
        Assert.Equal(0x50, match.Address);
        Assert.Equal("GalaxGPUController", match.Driver);
    }

    [Fact]
    public void UnknownCardResolvesToNothing() => Assert.Empty(GpuCatalog.Find(0x10DE, 0x2786, 0x1234, 0x5678));

    [Fact]
    public void MatchNeedsAllFourIds() => Assert.Empty(GpuCatalog.Find(0x10DE, 0x2786, 0x10DE, 0x1876));
}
