using FLARE.Core;

namespace FLARE.Tests;

public class ReportGeneratorTests
{
    [Theory]
    [InlineData("32.0.15.8129", "581.29")]
    [InlineData("32.0.15.6293", "562.93")]
    [InlineData("31.0.15.5599", "555.99")]
    [InlineData("31.0.15.2802", "528.02")]
    [InlineData("30.0.15.1179", "511.79")]
    public void ToNvidiaVersion_StandardVersions_ConvertsCorrectly(string input, string expected)
    {
        Assert.Equal(expected, ReportGenerator.ToNvidiaVersion(input));
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("1.2.3")]
    [InlineData("")]
    public void ToNvidiaVersion_MalformedInput_ReturnsOriginal(string input)
    {
        Assert.Equal(input, ReportGenerator.ToNvidiaVersion(input));
    }

    [Fact]
    public void ToNvidiaVersion_ShortFourthPart_ReturnsOriginal()
    {
        Assert.Equal("1.0.1.23", ReportGenerator.ToNvidiaVersion("1.0.1.23"));
    }

    private static GpuInfo TestGpu() =>
        new("RTX 4090", "32.0.15.8129", "95.02.18.80.C1", "0x1234", "GPU-abc",
            "0000:01:00.0", 128, "24576 MB");

    [Fact]
    public void Generate_EmptyErrors_ReportsNoneFound()
    {
        var report = ReportGenerator.Generate(TestGpu(), []);

        Assert.Contains("No nvlddmkm errors found in Windows Event Log.", report);
        Assert.DoesNotContain("Errors by SM location:", report);
    }

    [Fact]
    public void Generate_ErrorsWithSmCoords_IncludesProbabilityAnalysis()
    {
        var errors = new List<NvlddmkmError>
        {
            new(new DateTime(2025, 1, 1), 13, "msg", 3, 1, 0, "Illegal Instruction Encoding"),
            new(new DateTime(2025, 1, 2), 13, "msg", 3, 1, 0, "Illegal Instruction Encoding"),
            new(new DateTime(2025, 1, 3), 13, "msg", 3, 1, 0, "Page Fault"),
        };

        var report = ReportGenerator.Generate(TestGpu(), errors);

        Assert.Contains("Errors by SM location:", report);
        Assert.Contains("GPC 3, TPC 1, SM 0", report);
        Assert.Contains("effectively zero", report);
        Assert.Contains("not randomly distributed", report);
    }

    [Fact]
    public void Generate_SectionNumberingSequential_WhenOptionalSectionsAbsent()
    {
        var errors = new List<NvlddmkmError>
        {
            new(new DateTime(2025, 1, 1), 13, "msg", 3, 1, 0, "Page Fault"),
        };

        var report = ReportGenerator.Generate(TestGpu(), errors);

        // Required sections: 1=GPU, 2=Errors, then timeline, then summary.
        // With no crashes/appCrashes/drivers/dumpAnalysis, sections must still number without gaps.
        Assert.Contains("1. GPU IDENTIFICATION", report);
        Assert.Contains("2. NVLDDMKM ERROR SUMMARY", report);
        Assert.Contains("3. ERROR TIMELINE", report);
        Assert.Contains("4. SUMMARY", report);
    }

    [Fact]
    public void Generate_AppCrashCorrelation_SectionAppearsWhenCorrelated()
    {
        var ts = new DateTime(2025, 1, 15, 10, 0, 0);
        var errors = new List<NvlddmkmError>
        {
            new(ts, 13, "msg", 3, 1, 0, "Page Fault"),
        };
        var appCrashes = new List<EventLogParser.AppCrashEvent>
        {
            new(ts.AddSeconds(5), "game.exe", "nvlddmkm.sys", "game.exe (faulting module: nvlddmkm.sys)"),
        };

        var report = ReportGenerator.Generate(TestGpu(), errors, null, appCrashes);

        Assert.Contains("APPLICATION CRASH CORRELATION", report);
        Assert.Contains("game.exe", report);
    }

    [Fact]
    public void Generate_DriverInstallHistory_SectionAppearsWithData()
    {
        var errors = new List<NvlddmkmError>
        {
            new(new DateTime(2025, 1, 1), 13, "msg", 3, 1, 0, "Page Fault"),
        };
        var drivers = new List<EventLogParser.DriverInstallEvent>
        {
            new(new DateTime(2024, 12, 1), "32.0.15.8129", "setupapi: 32.0.15.8129"),
        };

        var report = ReportGenerator.Generate(TestGpu(), errors, null, null, drivers);

        Assert.Contains("DRIVER INSTALL HISTORY", report);
        Assert.Contains("581.29", report); // Formatted NVIDIA version
        Assert.Contains("32.0.15.8129", report);
    }

    [Fact]
    public void Generate_CrashEvents_SectionAppearsWithData()
    {
        var errors = new List<NvlddmkmError>();
        var crashes = new List<SystemCrashEvent>
        {
            new(new DateTime(2025, 2, 1, 3, 0, 0), "REBOOT", 41,
                "Unexpected reboot: VIDEO_TDR_FAILURE (GPU stopped responding) (code 0x00000116)"),
            new(new DateTime(2025, 2, 2, 4, 0, 0), "BSOD", 1001, "Fault bucket info"),
        };

        var report = ReportGenerator.Generate(TestGpu(), errors, crashes);

        Assert.Contains("SYSTEM CRASHES", report);
        Assert.Contains("Blue Screen crashes (BSOD):     1", report);
        Assert.Contains("Unexpected reboots:             1", report);
        Assert.Contains("VIDEO_TDR_FAILURE", report);
    }

    [Fact]
    public void Generate_DumpAnalysis_SectionAppearsWhenProvided()
    {
        var errors = new List<NvlddmkmError>();
        string dumpAnalysis = "  Analyzed 2 crash dump(s):\n  foo.dmp\n";

        var report = ReportGenerator.Generate(TestGpu(), errors, null, null, null, dumpAnalysis);

        Assert.Contains("CRASH DUMP ANALYSIS", report);
        Assert.Contains("foo.dmp", report);
    }
}
