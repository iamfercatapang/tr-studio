using CoreVision.Engine;
using Xunit;

public class CrashLogTests
{
    [Fact]
    public void Finds_a_crash_only_in_the_last_session()
    {
        string[] crashedBefore =
        [
            "2026-11-04 10:00:00.000 Launch: admin=False, args=[]",
            "2026-11-04 10:05:00.000 Crash: System.InvalidOperationException: boom",
            "2026-11-04 10:06:00.000 Launch: admin=False, args=[]",
            "2026-11-04 10:07:00.000 Quit from the tray",
        ];
        Assert.False(CrashLog.LastSessionCrashed(crashedBefore));
        Assert.True(CrashLog.LastSessionCrashed(crashedBefore[..2]));
        Assert.False(CrashLog.LastSessionCrashed([]));
    }

    [Fact]
    public void Takes_the_user_and_pc_out_of_a_report()
    {
        var text = @"Could not open C:\Users\Fer Kevin\AppData\Local\x.json on FERS-PC for fer. Also C:/Users/someone/Desktop/a.png";
        var scrubbed = CrashLog.Scrub(text, "fer", "FERS-PC", @"C:\Users\Fer Kevin");
        Assert.DoesNotContain("Fer Kevin", scrubbed);
        Assert.DoesNotContain("FERS-PC", scrubbed);
        Assert.DoesNotContain(" fer.", scrubbed);
        Assert.DoesNotContain("someone", scrubbed);
        Assert.Contains(@"%USERPROFILE%\AppData\Local\x.json", scrubbed);
        Assert.Contains("%COMPUTERNAME%", scrubbed);
    }

    [Fact]
    public void Leaves_shared_folders_and_ordinary_words_alone()
    {
        var text = @"C:\Users\Public\Documents and the word ferry";
        Assert.Equal(text, CrashLog.Scrub(text, "fer", "PC", @"C:\Users\fer"));
    }
}
