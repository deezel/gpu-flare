using FLARE.UI.Helpers;

namespace FLARE.Tests;

public class BuildBannerTests
{
    [Fact]
    public void Format_ReleaseTag_HasNoBuildMarker()
    {
        var title = BuildBanner.Format("0.7.0+abc1234", isRelease: true);

        Assert.Equal("FLARE 0.7.0 - Fault Log Analysis & Reboot Examination", title);
        Assert.DoesNotContain("DEV BUILD", title);
        Assert.DoesNotContain("SNAPSHOT", title);
        Assert.DoesNotContain("NON-RELEASE", title);
    }

    [Fact]
    public void Format_LocalDevBuild_GetsDevBuildMarker()
    {
        var title = BuildBanner.Format("0.7.0+dev", isRelease: false);

        Assert.StartsWith("[DEV BUILD]", title);
        Assert.Contains("0.7.0+dev", title);
    }

    [Fact]
    public void Format_CiSnapshotBuild_GetsSnapshotMarker()
    {
        var title = BuildBanner.Format("0.7.0+abc1234", isRelease: false);

        Assert.StartsWith("[SNAPSHOT]", title);
        Assert.Contains("0.7.0+abc1234", title);
    }

    [Fact]
    public void Format_NonReleaseWithNoHash_StillMarked()
    {
        var title = BuildBanner.Format("0.7.0", isRelease: false);

        Assert.StartsWith("[NON-RELEASE]", title);
        Assert.Contains("0.7.0", title);
    }

    [Fact]
    public void Format_DevHashCaseInsensitive()
    {
        Assert.StartsWith("[DEV BUILD]", BuildBanner.Format("0.7.0+DEV", isRelease: false));
        Assert.StartsWith("[DEV BUILD]", BuildBanner.Format("0.7.0+Dev", isRelease: false));
    }

    [Fact]
    public void Format_ReleaseStripsHash()
    {
        var title = BuildBanner.Format("0.7.0+abc1234", isRelease: true);

        Assert.DoesNotContain("abc1234", title);
        Assert.DoesNotContain("+", title);
    }

    [Fact]
    public void FormatAboutVersionLine_Release_NoHash()
    {
        var line = BuildBanner.FormatAboutVersionLine("0.7.0+abc1234", isRelease: true);

        Assert.Equal("Version 0.7.0 · release", line);
        Assert.DoesNotContain("abc1234", line);
    }

    [Fact]
    public void FormatAboutVersionLine_LocalDev_HashImpliedNotRepeated()
    {
        var line = BuildBanner.FormatAboutVersionLine("0.7.0+dev", isRelease: false);

        Assert.Equal("Version 0.7.0 · local dev build", line);
    }

    [Fact]
    public void FormatAboutVersionLine_Snapshot_HashAppearsOnceInParens()
    {
        var line = BuildBanner.FormatAboutVersionLine("0.7.0+abc1234", isRelease: false);

        Assert.Equal("Version 0.7.0 (abc1234) · snapshot", line);
    }

    [Fact]
    public void FormatAboutVersionLine_NoHashNoFlag()
    {
        var line = BuildBanner.FormatAboutVersionLine("0.7.0", isRelease: false);

        Assert.Equal("Version 0.7.0 · non-release build", line);
    }
}
