using FLARE.Core;

namespace FLARE.Tests;

public class GpuInfoTests
{
    const string SampleQueryOutput = @"
GPU 00000000:01:00.0
    Product Name                          : NVIDIA GeForce RTX 5090
    GPU Link Info
        PCIe Generation
            Max                               : 5
            Current                           : 4
            Device Current                    : 4
            Device Max                        : 5
            Host Max                          : 5
        Link Width
            Max                               : 16x
            Current                           : 16x
    Other Section
        foo                                   : bar
";

    const string DegradedQueryOutput = @"
    GPU Link Info
        PCIe Generation
            Max                               : 5
            Current                           : 3
        Link Width
            Max                               : 16x
            Current                           : 8x
";

    [Fact]
    public void ParsePcieFromQueryOutput_ExtractsGenAndWidth()
    {
        var (curGen, maxGen, curW, maxW) = GpuInfo.ParsePcieFromQueryOutput(SampleQueryOutput);
        Assert.Equal(4, curGen);
        Assert.Equal(5, maxGen);
        Assert.Equal(16, curW);
        Assert.Equal(16, maxW);
    }

    [Fact]
    public void ParsePcieFromQueryOutput_DegradedLink_ReturnsActualValues()
    {
        var (curGen, maxGen, curW, maxW) = GpuInfo.ParsePcieFromQueryOutput(DegradedQueryOutput);
        Assert.Equal(3, curGen);
        Assert.Equal(5, maxGen);
        Assert.Equal(8, curW);
        Assert.Equal(16, maxW);
    }

    [Fact]
    public void ParsePcieFromQueryOutput_MissingSection_ReturnsZeros()
    {
        var (curGen, maxGen, curW, maxW) = GpuInfo.ParsePcieFromQueryOutput("no link info here");
        Assert.Equal(0, curGen);
        Assert.Equal(0, maxGen);
        Assert.Equal(0, curW);
        Assert.Equal(0, maxW);
    }

    [Fact]
    public void ParsePcieFromQueryOutput_EmptyString_ReturnsZeros()
    {
        var (curGen, maxGen, curW, maxW) = GpuInfo.ParsePcieFromQueryOutput("");
        Assert.Equal(0, curGen);
        Assert.Equal(0, maxGen);
        Assert.Equal(0, curW);
        Assert.Equal(0, maxW);
    }

    const string Bar1EnabledOutput = @"
    BAR1 Memory Usage
        Total                             : 32768 MiB
        Used                              : 2 MiB
        Free                              : 32766 MiB
";

    const string Bar1DefaultOutput = @"
    BAR1 Memory Usage
        Total                             : 256 MiB
        Used                              : 2 MiB
        Free                              : 254 MiB
";

    [Fact]
    public void ParseBar1Total_RebarEnabled_ReturnsFullGpuMemory()
    {
        Assert.Equal(32768, GpuInfo.ParseBar1TotalMibFromQueryOutput(Bar1EnabledOutput));
    }

    [Fact]
    public void ParseBar1Total_RebarDisabled_Returns256()
    {
        Assert.Equal(256, GpuInfo.ParseBar1TotalMibFromQueryOutput(Bar1DefaultOutput));
    }

    [Fact]
    public void ParseBar1Total_MissingSection_ReturnsZero()
    {
        Assert.Equal(0, GpuInfo.ParseBar1TotalMibFromQueryOutput("no bar1 section"));
    }

    // Trusted-path precedence for nvidia-smi. Mirrors FindCdbTests — we pin the
    // ordering behavior without hitting the filesystem so the "only trust absolute
    // System32 path" invariant survives future refactors.

    [Fact]
    public void FindNvidiaSmiInTrustedPaths_FirstExistingWins()
    {
        string[] paths = ["A", "B", "C"];
        var result = GpuInfo.FindNvidiaSmiInTrustedPaths(paths, p => p == "B" || p == "C");
        Assert.Equal("B", result);
    }

    [Fact]
    public void FindNvidiaSmiInTrustedPaths_NoneExist_ReturnsNull()
    {
        var result = GpuInfo.FindNvidiaSmiInTrustedPaths(["X", "Y"], _ => false);
        Assert.Null(result);
    }

    [Fact]
    public void FindNvidiaSmiInTrustedPaths_EmptyList_ReturnsNull()
    {
        var result = GpuInfo.FindNvidiaSmiInTrustedPaths([], _ => true);
        Assert.Null(result);
    }

    [Fact]
    public void WarnIfQueryLayoutDrift_BothBlocksMissing_FiresTwoCanaries()
    {
        var health = new CollectorHealth();
        GpuInfo.WarnIfQueryLayoutDrift("some unrelated output",
            pcieCurrentGen: 0, pcieMaxGen: 0, pcieCurrentWidth: 0, pcieMaxWidth: 0,
            bar1TotalMib: 0, log: null, health);

        Assert.Equal(2, health.Notices.Count);
        Assert.All(health.Notices, n =>
        {
            Assert.Equal(CollectorNoticeKind.Canary, n.Kind);
            Assert.Equal("nvidia-smi -q layout", n.Source);
        });
        Assert.Contains(health.Notices, n => n.Message.Contains("GPU Link Info"));
        Assert.Contains(health.Notices, n => n.Message.Contains("BAR1 Memory Usage"));
    }

    [Fact]
    public void WarnIfQueryLayoutDrift_BlocksPresent_FiresNothing()
    {
        var health = new CollectorHealth();
        GpuInfo.WarnIfQueryLayoutDrift(SampleQueryOutput + Bar1EnabledOutput,
            pcieCurrentGen: 4, pcieMaxGen: 5, pcieCurrentWidth: 16, pcieMaxWidth: 16,
            bar1TotalMib: 32768, log: null, health);

        Assert.Empty(health.Notices);
    }

    [Fact]
    public void WarnIfQueryLayoutDrift_BlockPresentButParseFailed_FiresCanary()
    {
        var health = new CollectorHealth();
        GpuInfo.WarnIfQueryLayoutDrift("GPU Link Info\nBAR1 Memory Usage\n(garbled inner content)",
            pcieCurrentGen: 0, pcieMaxGen: 0, pcieCurrentWidth: 0, pcieMaxWidth: 0,
            bar1TotalMib: 0, log: null, health);

        Assert.Equal(2, health.Notices.Count);
        Assert.Contains(health.Notices, n => n.Message.Contains("PCIe link values"));
        Assert.Contains(health.Notices, n => n.Message.Contains("Total MiB"));
    }
}
