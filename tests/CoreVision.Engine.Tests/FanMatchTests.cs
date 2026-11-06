using CoreVision.Engine;
using Xunit;

public class FanMatchTests
{
    static List<FanHeader> Headers(params string[] names) =>
        names.Select((name, i) => new FanHeader($"/lpc/chip/0/fan/{i}", name, 1000 + i)).ToList();

    [Fact]
    public void Msi_names_are_matched_by_what_they_say()
    {
        var headers = Headers("CPU Fan", "Pump Fan", "System Fan #1", "System Fan #2");
        Assert.Equal("Pump Fan", FanMatch.Guess(headers, FanRole.Pump)?.Name);
        Assert.Equal("CPU Fan", FanMatch.Guess(headers, FanRole.CpuFan)?.Name);
        Assert.Equal("System Fan #1", FanMatch.Guess(headers, FanRole.CaseFan)?.Name);
    }

    [Fact]
    public void Asus_names_skip_the_optional_cpu_header()
    {
        var headers = Headers("CPU_OPT", "CPU Fan", "AIO Pump", "Chassis Fan 1");
        Assert.Equal("AIO Pump", FanMatch.Guess(headers, FanRole.Pump)?.Name);
        Assert.Equal("CPU Fan", FanMatch.Guess(headers, FanRole.CpuFan)?.Name);
        Assert.Equal("Chassis Fan 1", FanMatch.Guess(headers, FanRole.CaseFan)?.Name);
    }

    [Fact]
    public void Numbered_headers_keep_the_old_picks()
    {
        var headers = Headers("Fan #1", "Fan #2", "Fan #3", "Fan #4", "Fan #5", "Fan #6", "Fan #7");
        Assert.Equal("Fan #7", FanMatch.Guess(headers, FanRole.Pump)?.Name);
        Assert.Equal("Fan #2", FanMatch.Guess(headers, FanRole.CpuFan)?.Name);
        Assert.Equal("Fan #3", FanMatch.Guess(headers, FanRole.CaseFan)?.Name);
    }

    [Fact]
    public void A_board_without_a_pump_header_shows_no_pump()
    {
        Assert.Null(FanMatch.Guess(Headers("CPU Fan", "System Fan #1"), FanRole.Pump));
        Assert.Null(FanMatch.Guess([], FanRole.CpuFan));
    }

    [Fact]
    public void The_users_pick_wins_and_none_hides_it()
    {
        var headers = Headers("CPU Fan", "Pump Fan", "System Fan #1");
        Assert.Equal("System Fan #1", FanMatch.Resolve(headers, new FanChoice(Pump: headers[2].Id), FanRole.Pump)?.Name);
        Assert.Null(FanMatch.Resolve(headers, new FanChoice(CaseFan: FanChoice.None), FanRole.CaseFan));
    }

    [Fact]
    public void A_pick_that_is_gone_falls_back_to_the_guess()
    {
        var headers = Headers("CPU Fan", "Pump Fan");
        Assert.Equal("Pump Fan", FanMatch.Resolve(headers, new FanChoice(Pump: "/lpc/other/0/fan/9"), FanRole.Pump)?.Name);
    }
}
