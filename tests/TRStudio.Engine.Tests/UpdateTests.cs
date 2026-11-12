using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using TRStudio.Engine;
using Xunit;

public class UpdateTests
{
    static readonly string Sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("installer"))).ToLowerInvariant();

    static JsonObject Asset(string name, string? digest = null, string? url = null) => new()
    {
        ["name"] = name,
        ["size"] = 9,
        ["digest"] = digest ?? $"sha256:{Sha}",
        ["browser_download_url"] = url ?? $"https://github.com/iamfercatapang/tr-studio/releases/download/v1.4.0/{name}",
    };

    static string Release(string tag = "v1.4.0", bool prerelease = false, params JsonObject[] assets) => new JsonObject
    {
        ["tag_name"] = tag,
        ["draft"] = false,
        ["prerelease"] = prerelease,
        ["body"] = "  Notes  ",
        ["assets"] = new JsonArray(assets.Length == 0 ? [Asset("TRStudio-1.4.0-Setup.exe")] : assets),
    }.ToJsonString();

    [Theory]
    [InlineData("v1.4.0", "1.4.0")]
    [InlineData("1.4", "1.4.0")]
    [InlineData("1.3.0.0", "1.3.0")]
    [InlineData("V2.0.1", "2.0.1")]
    public void Tags_and_assembly_versions_read_alike(string text, string expected)
    {
        Assert.Equal(Version.Parse(expected), AppUpdate.ParseVersion(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    public void A_tag_that_is_not_a_version_reads_as_none(string? text)
    {
        Assert.Null(AppUpdate.ParseVersion(text));
    }

    [Fact]
    public void A_release_offers_its_installer_with_its_hash()
    {
        var release = AppUpdate.Parse(Release());

        Assert.NotNull(release);
        Assert.Equal(new Version(1, 4, 0), release.Version);
        Assert.Equal("TRStudio-1.4.0-Setup.exe", release.InstallerName);
        Assert.Equal(Sha, release.Sha256);
        Assert.Equal("Notes", release.Notes);
        Assert.True(AppUpdate.IsNewer(release, new Version(1, 3, 0, 0)));
        Assert.False(AppUpdate.IsNewer(release, new Version(1, 4, 0, 0)));
    }

    [Fact]
    public void The_installer_is_picked_from_among_other_files()
    {
        var release = AppUpdate.Parse(Release(assets: [Asset("release-notes-1.4.0.md"), Asset("TRStudio-1.4.0-Setup.exe")]));

        Assert.Equal("TRStudio-1.4.0-Setup.exe", release?.InstallerName);
    }

    [Fact]
    public void A_prerelease_is_not_offered()
    {
        Assert.Null(AppUpdate.Parse(Release(prerelease: true)));
    }

    [Fact]
    public void An_installer_without_a_hash_is_not_offered()
    {
        Assert.Null(AppUpdate.Parse(Release(assets: Asset("TRStudio-1.4.0-Setup.exe", digest: ""))));
        Assert.Null(AppUpdate.Parse(Release(assets: Asset("TRStudio-1.4.0-Setup.exe", digest: "md5:abc"))));
    }

    [Fact]
    public void An_installer_hosted_anywhere_else_is_not_offered()
    {
        Assert.Null(AppUpdate.Parse(Release(assets: Asset("TRStudio-1.4.0-Setup.exe", url: "https://example.com/TRStudio-1.4.0-Setup.exe"))));
        Assert.Null(AppUpdate.Parse(Release(assets: Asset("TRStudio-1.4.0-Setup.exe", url: "https://github.com/someone/else/releases/download/v1.4.0/TRStudio-1.4.0-Setup.exe"))));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"message":"Not Found"}""")]
    [InlineData("""{"tag_name":"v1.4.0","assets":"oops"}""")]
    public void A_reply_that_is_not_a_release_offers_nothing(string json)
    {
        Assert.Null(AppUpdate.Parse(json));
    }

    [Fact]
    public void Only_the_exact_download_matches()
    {
        var release = AppUpdate.Parse(Release())!;

        Assert.True(AppUpdate.Matches(SHA256.HashData(Encoding.UTF8.GetBytes("installer")), release));
        Assert.False(AppUpdate.Matches(SHA256.HashData(Encoding.UTF8.GetBytes("installer!")), release));
    }
}
