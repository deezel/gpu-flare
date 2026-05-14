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
        Assert.Equal(expected, NvidiaDriverVersion.ToNvidiaVersion(input));
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("1.2.3")]
    [InlineData("")]
    public void ToNvidiaVersion_MalformedInput_ReturnsOriginal(string input)
    {
        Assert.Equal(input, NvidiaDriverVersion.ToNvidiaVersion(input));
    }

    [Fact]
    public void ToNvidiaVersion_ShortFourthPart_ReturnsOriginal()
    {
        Assert.Equal("1.0.1.23", NvidiaDriverVersion.ToNvidiaVersion("1.0.1.23"));
    }

    [Theory]
    [InlineData("32.0.15.12345")] // future drift: 5-digit build — emit raw, not a wrong marketing number
    [InlineData("32.0.15.abcd")]
    [InlineData("32.0.1a.8129")]
    public void ToNvidiaVersion_ShapeDrift_ReturnsOriginal(string winVer)
    {
        Assert.Equal(winVer, NvidiaDriverVersion.ToNvidiaVersion(winVer));
    }

    private static GpuInfo TestGpu() =>
        new("RTX 4090", "32.0.15.8129", "95.02.18.80.C1", "0x1234", "GPU-abc",
            "0000:01:00.0", 128, "24576 MB", 0, 0, 0, 0, 0, 1);

    [Fact]
    public void Generate_EmptyErrors_ReportsNoneFound()
    {
        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, [])).Main;

        Assert.Contains("No nvlddmkm errors found in Windows Event Log.", report);
        Assert.DoesNotContain("Errors by SM location:", report);
    }

    [Fact]
    public void Generate_ErrorsWithSmCoords_StrongEvidence_IncludesConcentrationAnalysis()
    {
        var errors = new List<NvlddmkmError>();
        for (int i = 0; i < 12; i++)
            errors.Add(new(new DateTime(2025, 1, 1).AddDays(i), 13, "msg", 3, 1, 0, "Illegal Instruction Encoding"));

        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, errors)).Main;

        Assert.Contains("Errors by SM location:", report);
        Assert.Contains("GPC 3, TPC 1, SM 0", report);
        Assert.Contains("tight recurring cluster", report);
        Assert.Contains("troubleshooting lead", report);
        Assert.Contains("not a conclusion", report);
        Assert.DoesNotContain("effectively zero", report);
        Assert.DoesNotContain("10^", report);
        Assert.DoesNotContain("treat as anecdotal", report);
    }

    [Fact]
    public void Generate_ErrorsWithSmCoords_WeakEvidence_FlagsAsAnecdotal()
    {
        var errors = new List<NvlddmkmError>
        {
            new(new DateTime(2025, 1, 1), 13, "msg", 3, 1, 0, "Illegal Instruction Encoding"),
            new(new DateTime(2025, 1, 2), 13, "msg", 3, 1, 0, "Illegal Instruction Encoding"),
            new(new DateTime(2025, 1, 3), 13, "msg", 3, 1, 0, "Page Fault"),
        };

        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, errors)).Main;

        Assert.Contains("Errors by SM location:", report);
        Assert.Contains("GPC 3, TPC 1, SM 0", report);
        Assert.Contains("treat as an anecdotal signal", report);
        Assert.DoesNotContain("tight recurring cluster", report);
        Assert.DoesNotContain("troubleshooting lead", report);
        Assert.DoesNotContain("repeated concentration of errors", report);
    }

    [Fact]
    public void Generate_ErrorsWithSmCoords_SingleEvent_FlagsAsAnecdotal()
    {
        var errors = new List<NvlddmkmError>
        {
            new(new DateTime(2025, 1, 1), 13, "msg", 3, 1, 0, "Illegal Instruction Encoding"),
        };

        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, errors)).Main;

        Assert.Contains("Errors by SM location:", report);
        Assert.Contains("GPC 3, TPC 1, SM 0", report);
        Assert.Contains("treat as an anecdotal signal", report);
        Assert.DoesNotContain("tight recurring cluster", report);
        Assert.DoesNotContain("troubleshooting lead", report);
        Assert.DoesNotContain("repeated concentration of errors", report);
    }

    [Fact]
    public void Generate_ErrorsWithSmCoords_LargeButSpreadOut_FlagsAsAnecdotal()
    {
        // Strong evidence requires both sample size AND tightness — 20 errors
        // across 10 SMs averages 2/SM, below the 4/SM threshold.
        var errors = new List<NvlddmkmError>();
        for (int sm = 0; sm < 10; sm++)
            for (int rep = 0; rep < 2; rep++)
                errors.Add(new(new DateTime(2025, 1, 1).AddHours(sm * 24 + rep),
                    13, "msg", 3, 1, sm, "Page Fault"));

        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, errors)).Main;

        Assert.DoesNotContain("tight recurring cluster", report);
        Assert.Contains("treat as an anecdotal signal", report);
    }

    [Fact]
    public void Generate_ErrorsWithSmCoords_RepeatedButAcrossTooManyLocations_FlagsBroadCluster()
    {
        var errors = new List<NvlddmkmError>();
        for (int sm = 0; sm < 5; sm++)
            for (int rep = 0; rep < 4; rep++)
                errors.Add(new(new DateTime(2025, 1, 1).AddHours(sm * 24 + rep),
                    13, "msg", 3, 1, sm, "Page Fault"));

        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, errors)).Main;

        Assert.Contains("too many SMs", report);
        Assert.Contains("clustered signal", report);
        Assert.DoesNotContain("tight recurring cluster", report);
        Assert.DoesNotContain("troubleshooting lead", report);
    }

    [Fact]
    public void Generate_ErrorsWithSmCoords_LargeButSpreadOut_DoesNotClaimSampleTooSmall()
    {
        var errors = new List<NvlddmkmError>();
        for (int sm = 0; sm < 10; sm++)
            for (int rep = 0; rep < 2; rep++)
                errors.Add(new(new DateTime(2025, 1, 1).AddHours(sm * 24 + rep),
                    13, "msg", 3, 1, sm, "Page Fault"));

        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, errors)).Main;

        Assert.Contains("20 coordinate-tagged error(s)", report);
        Assert.DoesNotContain("Sample below the 10-error threshold", report);
        Assert.DoesNotContain("Sample size is below the threshold", report);
        Assert.Contains("spread across too many SMs", report);
    }

    [Fact]
    public void Generate_ErrorsWithSmCoords_TinySample_QuotesSampleSizeReason()
    {
        var errors = new List<NvlddmkmError>
        {
            new(new DateTime(2025, 1, 1), 13, "msg", 3, 1, 0, "Page Fault"),
            new(new DateTime(2025, 1, 2), 13, "msg", 3, 1, 0, "Page Fault"),
            new(new DateTime(2025, 1, 3), 13, "msg", 3, 1, 0, "Page Fault"),
        };

        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, errors)).Main;

        Assert.Contains("Sample below the 10-error threshold", report);
        Assert.DoesNotContain("spread across too many SMs", report);
        Assert.Contains("treat as an anecdotal signal", report);
    }

    [Fact]
    public void Generate_SectionNumberingSequential_WhenOptionalSectionsAbsent()
    {
        var errors = new List<NvlddmkmError>
        {
            new(new DateTime(2025, 1, 1), 13, "msg", 3, 1, 0, "Page Fault"),
        };

        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, errors)).Main;

        Assert.Contains("## GPU IDENTIFICATION", report);
        Assert.Contains("## NVLDDMKM ERROR SUMMARY", report);
        Assert.Contains("## ERROR TIMELINE", report);
        Assert.Contains("## SUMMARY", report);
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

        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, errors, null, appCrashes)).Main;

        Assert.Contains("APPLICATION CRASH CORRELATION", report);
        Assert.Contains("game.exe", report);
        Assert.Contains("1 application crash(es) matched 1 GPU error(s)", report);
        Assert.Contains("1 correlation pair(s)", report);
        Assert.Contains("GPU error; 2025-01-15 10:00:05  game.exe (nvlddmkm.sys) [5s after GPU error]", report);
        Assert.DoesNotContain("GPU error then", report);
    }

    [Fact]
    public void Generate_AppCrashCorrelation_CrashBeforeGpuError_RendersDirection()
    {
        var ts = new DateTime(2025, 1, 15, 10, 0, 0);
        var errors = new List<NvlddmkmError>
        {
            new(ts, 13, "msg", 3, 1, 0, "Page Fault"),
        };
        var appCrashes = new List<EventLogParser.AppCrashEvent>
        {
            new(ts.AddSeconds(-10), "game.exe", "nvlddmkm.sys", "game.exe crashed"),
        };

        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, errors, null, appCrashes)).Main;

        Assert.Contains("GPU error; 2025-01-15 09:59:50  game.exe (nvlddmkm.sys) [10s before GPU error]", report);
        Assert.DoesNotContain("GPU error then", report);
    }

    [Fact]
    public void Generate_AppCrashCorrelation_BurstOfGpuErrors_DoesNotInflateCrashCount()
    {
        // One app crash sitting inside a burst of 5 GPU errors produces 5
        // correlation pairs. The report must say "1 crash matched 5 errors",
        // not "5 crashes".
        var baseTime = new DateTime(2025, 1, 15, 10, 0, 0);
        var errors = new List<NvlddmkmError>
        {
            new(baseTime.AddSeconds(0),  13, "msg", 3, 1, 0, "Page Fault"),
            new(baseTime.AddSeconds(5),  13, "msg", 3, 1, 0, "Page Fault"),
            new(baseTime.AddSeconds(10), 13, "msg", 3, 1, 0, "Page Fault"),
            new(baseTime.AddSeconds(15), 13, "msg", 3, 1, 0, "Page Fault"),
            new(baseTime.AddSeconds(20), 13, "msg", 3, 1, 0, "Page Fault"),
        };
        var appCrashes = new List<EventLogParser.AppCrashEvent>
        {
            new(baseTime.AddSeconds(12), "game.exe", "nvlddmkm.sys", "game.exe crashed"),
        };

        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, errors, null, appCrashes)).Main;

        Assert.Contains("1 application crash(es) matched 5 GPU error(s)", report);
        Assert.Contains("5 correlation pair(s)", report);
        Assert.Contains("| `game.exe` | 1 | 5 |", report);
    }

    [Fact]
    public void Generate_AppCrashCorrelation_ManyPairs_CapsRenderedRows()
    {
        // Pathological scenario the finding calls out: a single noisy app crash
        // within a GPU-error burst exploding into many pairs. Per-pair rows must
        // cap so the detail table doesn't bury the summary above it — the
        // distinct-crash and pair-count lines still reflect the true totals.
        var baseTime = new DateTime(2025, 1, 15, 10, 0, 0);
        var errors = new List<NvlddmkmError>();
        for (int i = 0; i < 250; i++)
            errors.Add(new(baseTime.AddSeconds(i * 0.1), 13, "msg", 3, 1, 0, "Page Fault"));
        var appCrashes = new List<EventLogParser.AppCrashEvent>
        {
            new(baseTime.AddSeconds(12), "game.exe", "nvlddmkm.sys", "game.exe crashed"),
        };

        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, errors, null, appCrashes)).Main;

        Assert.Contains("250 correlation pair(s)", report);
        Assert.Contains("further pair(s) omitted", report);
        Assert.Contains("150 further pair(s) omitted", report);
    }

    [Fact]
    public void Generate_AppCrashCorrelation_SmallSet_DoesNotEmitOmissionLine()
    {
        var baseTime = new DateTime(2025, 1, 15, 10, 0, 0);
        var errors = new List<NvlddmkmError>
        {
            new(baseTime, 13, "msg", 3, 1, 0, "Page Fault"),
        };
        var appCrashes = new List<EventLogParser.AppCrashEvent>
        {
            new(baseTime.AddSeconds(5), "game.exe", "nvlddmkm.sys", "game.exe crashed"),
        };

        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, errors, null, appCrashes)).Main;

        Assert.DoesNotContain("further pair(s) omitted", report);
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

        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, errors, null, null, drivers)).Main;

        Assert.Contains("DRIVER INSTALL HISTORY", report);
        Assert.Contains("581.29", report); // Formatted NVIDIA version
        Assert.Contains("32.0.15.8129", report);
        Assert.Contains("Counts include all collected nvlddmkm events", report);
        Assert.Contains("whether or not SM coordinates were present", report);
    }

    [Fact]
    public void Generate_DriverInstallHistory_MultiGpu_AnnotatesSystemWideCounts()
    {
        var gpu = TestGpu() with { NvidiaDeviceCount = 2 };
        var errors = new List<NvlddmkmError>
        {
            new(new DateTime(2025, 1, 1), 13, "msg", 3, 1, 0, "Page Fault"),
        };
        var drivers = new List<EventLogParser.DriverInstallEvent>
        {
            new(new DateTime(2024, 12, 1), "32.0.15.8129", "setupapi: 32.0.15.8129"),
        };

        var report = ReportGenerator.Generate(new ReportInput(gpu, null, errors, null, null, drivers)).Main;

        Assert.Contains("DRIVER INSTALL HISTORY", report);
        Assert.Contains("2 NVIDIA adapters detected", report);
        Assert.Contains("system-wide and cannot be attributed to a specific adapter", report);
    }

    [Fact]
    public void Generate_DriverInstallHistory_SingleGpu_OmitsMultiGpuCaveat()
    {
        var errors = new List<NvlddmkmError>
        {
            new(new DateTime(2025, 1, 1), 13, "msg", 3, 1, 0, "Page Fault"),
        };
        var drivers = new List<EventLogParser.DriverInstallEvent>
        {
            new(new DateTime(2024, 12, 1), "32.0.15.8129", "setupapi: 32.0.15.8129"),
        };

        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, errors, null, null, drivers)).Main;

        Assert.Contains("DRIVER INSTALL HISTORY", report);
        Assert.DoesNotContain("NVIDIA adapters detected", report);
        Assert.DoesNotContain("cannot be attributed to a specific adapter", report);
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

        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, errors, crashes)).Main;

        Assert.Contains("SYSTEM CRASHES", report);
        Assert.Contains("**Blue Screen crashes (BSOD):** 1", report);
        Assert.Contains("**Unexpected reboots:** 1", report);
        Assert.Contains("VIDEO_TDR_FAILURE", report);
    }

    [Fact]
    public void Generate_RebootCauses_DescriptionWithEmbeddedParentheses_GroupedByFullName()
    {
        var crashes = new List<SystemCrashEvent>
        {
            new(new DateTime(2025, 2, 1), "REBOOT", 41,
                "Unexpected reboot: VIDEO_TDR_FAILURE (GPU stopped responding) (code 0x00000116)"),
            new(new DateTime(2025, 2, 2), "REBOOT", 41,
                "Unexpected reboot: VIDEO_TDR_FAILURE (GPU stopped responding) (code 0x00000116)"),
        };

        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, [], crashes)).Main;

        Assert.Contains("VIDEO_TDR_FAILURE (GPU stopped responding)", report);
        Assert.Matches(@"VIDEO_TDR_FAILURE \(GPU stopped responding\): 2", report);
    }

    [Fact]
    public void Generate_RebootCauses_MalformedDescriptionFallsBackToFullString()
    {
        var crashes = new List<SystemCrashEvent>
        {
            new(new DateTime(2025, 2, 1), "REBOOT", 41, "Unexpected reboot: something without a code sentinel"),
        };

        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, [], crashes)).Main;

        Assert.Contains("Unexpected reboot: something without a code sentinel", report);
    }

    [Fact]
    public void Generate_RebootCorrelationProse_QuotesSameWindowAsThreshold()
    {
        var ts = new DateTime(2025, 1, 15, 10, 0, 0);
        var window = ReportGenerator.RebootCorrelationWindowMinutes;
        var errors = new List<NvlddmkmError>
        {
            new(ts, 13, "msg", 3, 1, 0, "Page Fault"),
        };
        var crashes = new List<SystemCrashEvent>
        {
            new(ts.AddMinutes(window - 1), "REBOOT", 41, "Unexpected reboot: foo (code 0x00000001)"),
        };

        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, errors, crashes)).Main;

        Assert.Contains($"{window} minutes of an nvlddmkm GPU error", report);
        Assert.Contains($"{window}-minute window", report);
    }

    [Fact]
    public void Generate_RebootJustOutsideWindow_DoesNotCorrelate()
    {
        var ts = new DateTime(2025, 1, 15, 10, 0, 0);
        var window = ReportGenerator.RebootCorrelationWindowMinutes;
        var errors = new List<NvlddmkmError>
        {
            new(ts, 13, "msg", 3, 1, 0, "Page Fault"),
        };
        var crashes = new List<SystemCrashEvent>
        {
            new(ts.AddMinutes(window + 1), "REBOOT", 41, "Unexpected reboot: foo (code 0x00000001)"),
        };

        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, errors, crashes)).Main;

        Assert.DoesNotContain("Timing proximity:", report);
    }

    [Fact]
    public void Generate_SystemCrashes_MaxTimelineEntriesTruncates_EmitsOmittedNote()
    {
        var crashes = new List<SystemCrashEvent>
        {
            new(new DateTime(2025, 2, 1), "BSOD", 1001, "c1"),
            new(new DateTime(2025, 2, 2), "BSOD", 1001, "c2"),
            new(new DateTime(2025, 2, 3), "BSOD", 1001, "c3"),
        };

        var report = ReportGenerator.Generate(new ReportInput(
            TestGpu(), null, [], crashes, MaxTimelineEntries: 1)).Main;

        Assert.Contains("Crash timeline:", report);
        Assert.Contains("2 entries omitted by MaxTimelineEntries cap", report);
    }

    [Fact]
    public void Generate_SystemCrashes_NoTruncation_OmitsNote()
    {
        var crashes = new List<SystemCrashEvent>
        {
            new(new DateTime(2025, 2, 1), "BSOD", 1001, "c1"),
        };

        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, [], crashes)).Main;

        Assert.Contains("Crash timeline:", report);
        Assert.DoesNotContain("entries omitted by MaxTimelineEntries cap", report);
    }

    [Fact]
    public void Generate_DumpAnalysis_SectionAppearsWhenProvided()
    {
        var errors = new List<NvlddmkmError>();
        string dumpAnalysis = "  Analyzed 2 crash dump(s):\n  foo.dmp\n";

        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, errors, null, null, null, dumpAnalysis)).Main;

        Assert.Contains("CRASH DUMP ANALYSIS", report);
        Assert.Contains("foo.dmp", report);
    }

    [Fact]
    public void Generate_DumpAnalysis_CopiedZeroThisRun_AnnotatesStaleSource()
    {
        var report = ReportGenerator.Generate(new ReportInput(
            TestGpu(), null, [], null, null, null, "  Analyzed 2 crash dump(s):\n  foo.dmp\n",
            MinidumpsCopiedThisRun: 0)).Main;

        Assert.Contains("No new minidumps copied from system folder this run", report);
        Assert.Contains("staged by an earlier run", report);
        Assert.Contains("foo.dmp", report);
    }

    [Fact]
    public void Generate_DumpAnalysis_CopiedSomeThisRun_AnnotatesFreshCount()
    {
        var report = ReportGenerator.Generate(new ReportInput(
            TestGpu(), null, [], null, null, null, "  Analyzed 3 crash dump(s):\n  foo.dmp\n",
            MinidumpsCopiedThisRun: 2)).Main;

        Assert.Contains("2 new minidump(s) copied from system folder this run", report);
    }

    [Fact]
    public void Generate_DumpAnalysis_CopyNotAttempted_OmitsStaleOrFreshLine()
    {
        var report = ReportGenerator.Generate(new ReportInput(
            TestGpu(), null, [], null, null, null, "  Analyzed 1 crash dump(s):\n  foo.dmp\n")).Main;

        Assert.DoesNotContain("copied from system folder this run", report);
        Assert.Contains("foo.dmp", report);
    }

    [Fact]
    public void Generate_LiveKernelAnalysis_CopiedZeroThisRun_AnnotatesStaleSource()
    {
        var report = ReportGenerator.Generate(new ReportInput(
            TestGpu(), null, [], null, null, null,
            DumpAnalysis: null,
            LiveKernelAnalysis: "scanned 2 dump(s)",
            LiveKernelDumpsCopiedThisRun: 0)).Main;

        Assert.Contains("No new LiveKernel dumps copied from system folder this run", report);
        Assert.Contains("staged by an earlier run", report);
    }

    [Fact]
    public void Generate_LiveKernelAnalysis_CopiedSomeThisRun_AnnotatesFreshCount()
    {
        var report = ReportGenerator.Generate(new ReportInput(
            TestGpu(), null, [], null, null, null,
            DumpAnalysis: null,
            LiveKernelAnalysis: "scanned 3 dump(s)",
            LiveKernelDumpsCopiedThisRun: 4)).Main;

        Assert.Contains("4 new LiveKernel dump(s) copied from system folder this run", report);
    }

    [Fact]
    public void Generate_LiveKernelAnalysis_CopyNotAttempted_OmitsStaleOrFreshLine()
    {
        var report = ReportGenerator.Generate(new ReportInput(
            TestGpu(), null, [], null, null, null,
            DumpAnalysis: null,
            LiveKernelAnalysis: "scanned 1 dump(s)")).Main;

        Assert.DoesNotContain("LiveKernel dump(s) copied from system folder this run", report);
        Assert.DoesNotContain("No new LiveKernel dumps copied", report);
    }

    [Fact]
    public void Generate_FencedCodeBlockWithHeadingSyntax_DoesNotLeakIntoToc()
    {
        var injectedDumpBody = "```text\n## fake heading inside fence\n```\n";
        var report = ReportGenerator.Generate(new ReportInput(
            TestGpu(), null, [], null, null, null,
            DumpAnalysis: injectedDumpBody)).Main;

        var tocStart = report.IndexOf("## Contents", StringComparison.Ordinal);
        Assert.True(tocStart >= 0, "expected a Contents TOC");
        var tocEnd = report.IndexOf("## ", tocStart + 1, StringComparison.Ordinal);
        var toc = tocEnd > tocStart ? report.Substring(tocStart, tocEnd - tocStart) : report.Substring(tocStart);
        Assert.DoesNotContain("fake heading inside fence", toc);
    }

    [Fact]
    public void Generate_WithLiveKernelAnalysis_IncludesLiveKernelSectionAfterCrashDumpAnalysis()
    {
        var report = ReportGenerator.Generate(new ReportInput(
            Gpu: TestGpu(),
            System: null,
            Errors: new(),
            DumpAnalysis: "minidump analysis body",
            LiveKernelAnalysis: "live kernel analysis body")).Main;

        var crashIdx = report.IndexOf("CRASH DUMP ANALYSIS", StringComparison.Ordinal);
        var lkIdx    = report.IndexOf("LIVE KERNEL DUMP ANALYSIS", StringComparison.Ordinal);
        Assert.True(crashIdx > 0, "expected CRASH DUMP ANALYSIS header");
        Assert.True(lkIdx > crashIdx, "expected LIVE KERNEL DUMP ANALYSIS to follow CRASH DUMP ANALYSIS");
        Assert.Contains("live kernel analysis body", report);
    }

    [Fact]
    public void Generate_NoLiveKernelAnalysis_OmitsSection()
    {
        var report = ReportGenerator.Generate(new ReportInput(
            Gpu: TestGpu(),
            System: null,
            Errors: new())).Main;

        Assert.DoesNotContain("LIVE KERNEL DUMP ANALYSIS", report);
    }

    private static SystemInfo TestSystem() =>
        new(
            "American Megatrends", "F15a", "07/22/2025",
            "ASUSTeK", "ROG STRIX Z890-E", "Rev 1.00",
            "ASUS", "Desktop System",
            "Intel(R) Core(TM) i9-14900K",
            34359738368ul);

    [Fact]
    public void Generate_WithSystemInfo_IncludesIdentificationSection()
    {
        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), TestSystem(), [])).Main;

        Assert.Contains("## SYSTEM IDENTIFICATION", report);
        Assert.Contains("ROG STRIX Z890-E", report);
        Assert.Contains("i9-14900K", report);
        Assert.Contains("32.0 GB", report);
        Assert.Contains("## NVLDDMKM ERROR SUMMARY", report);
    }

    [Fact]
    public void Generate_WithoutSystemInfo_SkipsIdentificationSection()
    {
        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, [])).Main;

        Assert.DoesNotContain("SYSTEM IDENTIFICATION", report);
        Assert.Contains("## NVLDDMKM ERROR SUMMARY", report);
    }

    [Fact]
    public void Generate_PcieLinkBelowMax_MarksAsSampleTimeAndExplainsIdlePower()
    {
        var gpu = TestGpu() with { PcieCurrentGen = 3, PcieMaxGen = 5, PcieCurrentWidth = 8, PcieMaxWidth = 16 };
        var report = ReportGenerator.Generate(new ReportInput(gpu, null, [])).Main;

        Assert.Contains("[LOWER AT SAMPLE]", report);
        Assert.Contains("PCIe Gen:", report);
        Assert.Contains("PCIe Width:", report);
        Assert.Contains("sample-time link state", report);
        Assert.Contains("downshift", report);
        Assert.Contains("under load", report);
    }

    [Fact]
    public void Generate_MatchingPcieLink_DoesNotMarkBelowMax()
    {
        var gpu = TestGpu() with { PcieCurrentGen = 5, PcieMaxGen = 5, PcieCurrentWidth = 16, PcieMaxWidth = 16 };
        var report = ReportGenerator.Generate(new ReportInput(gpu, null, [])).Main;

        Assert.Contains("PCIe Gen:       5 (max 5)", report);
        Assert.DoesNotContain("BELOW MAX", report);
        Assert.DoesNotContain("LOWER AT SAMPLE", report);
    }

    [Fact]
    public void Generate_NoPcieLinkInfo_OmitsPcieLines()
    {
        // Default TestGpu() has all PCIe fields at 0 â€” lines should be omitted.
        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, [])).Main;

        Assert.DoesNotContain("PCIe Gen:", report);
        Assert.DoesNotContain("PCIe Width:", report);
        Assert.DoesNotContain("BAR1 Memory:", report);
    }

    [Fact]
    public void Generate_Bar1Small_ReportsReBarNotEnabled()
    {
        var gpu = TestGpu() with { Bar1TotalMib = 256 };
        var report = ReportGenerator.Generate(new ReportInput(gpu, null, [])).Main;

        Assert.Contains("BAR1 Memory:    256 MiB", report);
        Assert.Contains("Resizable BAR: not enabled", report);
    }

    [Fact]
    public void Generate_Bar1Large_ReportsReBarEnabled()
    {
        var gpu = TestGpu() with { Bar1TotalMib = 32768 };
        var report = ReportGenerator.Generate(new ReportInput(gpu, null, [])).Main;

        Assert.Contains("BAR1 Memory:    32768 MiB", report);
        Assert.Contains("Resizable BAR: enabled", report);
    }

    [Fact]
    public void Generate_RedactIdentifiers_HidesUuidAndMachineName()
    {
        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, [], RedactIdentifiers: true)).Main;

        Assert.DoesNotContain("GPU-abc", report);      // UUID from TestGpu
        Assert.Contains("[redacted]", report);
        // PCI bus is NOT redacted â€” it's generic (00000000:01:00.0 on most desktops)
        Assert.Contains("0000:01:00.0", report);
    }

    [Fact]
    public void Generate_RedactIdentifiers_PreservesAppNamesInCrashCorrelation()
    {
        // Process/driver names are the diagnostic signal; only UUID + machine
        // name + Windows user-profile paths are rewritten.
        var ts = new DateTime(2025, 1, 15, 10, 0, 0);
        var errors = new List<NvlddmkmError> { new(ts, 13, "msg", 3, 1, 0, "Page Fault") };
        var appCrashes = new List<EventLogParser.AppCrashEvent>
        {
            new(ts.AddSeconds(5), "game.exe", "nvlddmkm.sys", "game.exe crashed"),
        };

        var report = ReportGenerator.Generate(new ReportInput(
            TestGpu(), null, errors, null, appCrashes, RedactIdentifiers: true)).Main;

        Assert.Contains("game.exe", report);
        Assert.Contains("nvlddmkm.sys", report);
    }

    [Fact]
    public void Generate_RedactIdentifiers_ScrubsUserPathsInAppCrashRows()
    {
        var ts = new DateTime(2025, 1, 15, 10, 0, 0);
        var errors = new List<NvlddmkmError> { new(ts, 13, "msg", 3, 1, 0, "Page Fault") };
        var appCrashes = new List<EventLogParser.AppCrashEvent>
        {
            new(ts.AddSeconds(5),
                @"C:\Users\alice\AppData\Local\App\app.exe",
                @"C:\Users\alice\AppData\Local\App\native.dll",
                "desc"),
        };

        var report = ReportGenerator.Generate(new ReportInput(
            TestGpu(), null, errors, null, appCrashes, RedactIdentifiers: true)).Main;

        Assert.DoesNotContain("alice", report);
        Assert.Contains(@"%USERPROFILE%\AppData\Local\App\app.exe", report);
        Assert.Contains(@"%USERPROFILE%\AppData\Local\App\native.dll", report);
    }

    [Fact]
    public void Generate_RedactIdentifiers_ScrubsUserPathsInAllAppCrashesSection()
    {
        var ts = new DateTime(2025, 1, 15, 10, 0, 0);
        var appCrashes = new List<EventLogParser.AppCrashEvent>
        {
            new(ts, @"C:\Users\alice\Games\launcher.exe", "mod.dll", "desc"),
        };

        var report = ReportGenerator.Generate(new ReportInput(
            TestGpu(), null, [], null, appCrashes, RedactIdentifiers: true)).Main;

        Assert.DoesNotContain("alice", report);
        Assert.Contains(@"%USERPROFILE%\Games\launcher.exe", report);
    }

    [Fact]
    public void Generate_RedactIdentifiers_PreservesCdbLabelsAndValues()
    {
        var errors = new List<NvlddmkmError>();
        string dumpAnalysis =
            "    PROCESS_NAME:  firefox.exe\n" +
            "    IMAGE_NAME:  nvlddmkm.sys\n" +
            "    MODULE_NAME:  nv_driver\n" +
            "    FAULTING_MODULE: dxgkrnl.sys\n";

        var report = ReportGenerator.Generate(new ReportInput(
            TestGpu(), null, errors, null, null, null, dumpAnalysis, RedactIdentifiers: true)).Main;

        Assert.Contains("firefox.exe", report);
        Assert.Contains("nvlddmkm.sys", report);
        Assert.Contains("dxgkrnl.sys", report);
        Assert.Contains("nv_driver", report);
        Assert.Contains("CRASH DUMP ANALYSIS", report);
        Assert.Contains("PROCESS_NAME:", report);
    }

    [Fact]
    public void Generate_RedactIdentifiers_ScrubsUserPathsInCdbSummary()
    {
        // Catches the regression where RenderDumpAnalysis stops routing the cdb
        // summary through RedactCdbSummary — the unit tests on ScrubUserPaths only
        // prove the helper works, not that the renderer calls it.
        var dumpAnalysis =
            "    PROCESS_NAME:  firefox.exe\n" +
            "    SYMBOL_PATH: C:\\Users\\alice\\symbols;C:\\Windows\\System32\n" +
            "    MODULE_NAME:  nv_driver\n";

        var report = ReportGenerator.Generate(new ReportInput(
            TestGpu(), null, [], null, null, null, dumpAnalysis, RedactIdentifiers: true)).Main;

        Assert.DoesNotContain("alice", report);
        Assert.Contains(@"%USERPROFILE%\symbols", report);
        Assert.Contains("firefox.exe", report);
        Assert.Contains("nv_driver", report);
    }

    [Fact]
    public void Generate_RedactIdentifiers_HeaderDocumentsWhatWasScrubbed()
    {
        // The shareable report is the .txt; minidumps live under DO_NOT_SHARE
        // outside the report folder, so the header no longer hand-holds about
        // a "minidumps folder alongside this report". It does still explain
        // what redaction preserves vs. scrubs so a reader of the report
        // understands what the [redacted] markers replaced.
        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, [], RedactIdentifiers: true)).Main;

        Assert.Contains("[redacted]", report);
        Assert.Contains("**preserved**", report);
        Assert.DoesNotContain("minidumps/", report);
    }

    [Fact]
    public void Generate_MultiGpuSystem_SurfacesWarningInHeader()
    {
        var gpu = TestGpu() with { NvidiaDeviceCount = 2 };
        var report = ReportGenerator.Generate(new ReportInput(gpu, null, [])).Main;

        Assert.Contains("2 NVIDIA GPUs detected", report);
        Assert.Contains("RTX 4090", report);
        Assert.Contains("system-wide", report);
    }

    [Fact]
    public void Generate_SingleGpuSystem_OmitsMultiGpuWarning()
    {
        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, [])).Main;
        Assert.DoesNotContain("NVIDIA GPUs detected", report);
    }

    [Fact]
    public void Generate_MultiGpuSystem_SuppressesConcentrationAnalysis()
    {
        // nvlddmkm event coordinates don't identify which adapter emitted them, so
        // on a multi-GPU box we cannot honestly make adapter-specific conclusions
        // against the single GpuInfo we happened to pick. Raw SM-location grouping
        // still renders — that's a fact regardless of attribution.
        var gpu = TestGpu() with { NvidiaDeviceCount = 2 };
        var errors = new List<NvlddmkmError>
        {
            new(new DateTime(2025, 1, 1), 13, "msg", 3, 1, 0, "Illegal Instruction Encoding"),
            new(new DateTime(2025, 1, 2), 13, "msg", 3, 1, 0, "Illegal Instruction Encoding"),
            new(new DateTime(2025, 1, 3), 13, "msg", 3, 1, 0, "Page Fault"),
        };

        var report = ReportGenerator.Generate(new ReportInput(gpu, null, errors)).Main;

        Assert.Contains("Errors by SM location:", report);
        Assert.Contains("GPC 3, TPC 1, SM 0", report);
        Assert.Contains("Concentration analysis suppressed", report);
        Assert.Contains("cannot be attributed to a specific adapter", report);

        Assert.DoesNotContain("tight recurring cluster", report);
        Assert.DoesNotContain("troubleshooting lead", report);
        Assert.DoesNotContain("out of 128 total SMs", report);
        // The selected adapter's SM count must not appear in the Analysis block
        // either — printing it next to system-wide event counts invites the same
        // mental comparison the suppression text warns against.
        Assert.DoesNotContain("Total SMs on this GPU", report);
    }

    [Fact]
    public void Generate_MultiGpuSystem_SummaryPointsAtAmbiguity()
    {
        var gpu = TestGpu() with { NvidiaDeviceCount = 2 };
        var errors = new List<NvlddmkmError>
        {
            new(new DateTime(2025, 1, 1), 13, "msg", 3, 1, 0, "Page Fault"),
            new(new DateTime(2025, 1, 2), 13, "msg", 3, 1, 0, "Page Fault"),
        };

        var report = ReportGenerator.Generate(new ReportInput(gpu, null, errors)).Main;

        Assert.Contains("cannot be attributed to a specific adapter", report);
        Assert.Contains("isolate to one GPU", report);
        Assert.DoesNotContain("repeated concentration of errors", report);
    }

    [Fact]
    public void RedactCdbSummary_NullOrEmpty_ReturnsInput()
    {
        Assert.Equal("", ReportRedaction.RedactCdbSummary(""));
        Assert.Null(ReportRedaction.RedactCdbSummary(null!));
    }

    [Fact]
    public void RedactCdbSummary_PreservesLabelValuesAndStackFrames()
    {
        // Labels and stack frames are the diagnostic payload — not PII.
        var input =
            "    BUGCHECK_STR:  0x116\n" +
            "    PROCESS_NAME:  firefox.exe\n" +
            "    FAULTING_MODULE: fffff805`12345678 nvlddmkm.sys\n" +
            "    STACK_TEXT (top frames):\n" +
            "      fffff801`12345678 nvlddmkm!KeBugCheckEx+0x42\n" +
            "      fffff801`87654321 dxgkrnl!TdrBugcheckOnTimeout+0x10\n" +
            "    -----------------------\n";

        var redacted = ReportRedaction.RedactCdbSummary(input);

        Assert.Contains("PROCESS_NAME:  firefox.exe", redacted);
        Assert.Contains("FAULTING_MODULE: fffff805`12345678 nvlddmkm.sys", redacted);
        Assert.Contains("STACK_TEXT (top frames):", redacted);
        Assert.Contains("nvlddmkm!KeBugCheckEx", redacted);
        Assert.Contains("dxgkrnl!TdrBugcheckOnTimeout", redacted);
        Assert.Contains("-----------------------", redacted);
    }

    [Fact]
    public void RedactCdbSummary_ScrubsWindowsUsernameInPaths()
    {
        var input =
            "    IMAGE_NAME:  MyApp.exe\n" +
            "    SYMBOL_PATH: C:\\Users\\alice\\symbols;C:\\Windows\\System32\n";

        var redacted = ReportRedaction.RedactCdbSummary(input);

        Assert.DoesNotContain("alice", redacted);
        Assert.Contains(@"%USERPROFILE%\symbols", redacted);
        Assert.Contains(@"C:\Windows\System32", redacted);
        Assert.Contains("MyApp.exe", redacted);
    }

    [Fact]
    public void RedactCdbSummary_ScrubsGpuUuidsInTranscriptText()
    {
        // A GPU UUID appearing inside a cdb stack trace or symbol path must be
        // scrubbed for parity with RedactLogMessage — without this, a UUID
        // visible in the live log would be blanked but the saved .txt would leak it.
        var input =
            "    STACK_TEXT (top frames):\n" +
            "      nvlddmkm!SomeSymbol_GPU-12345678-1234-1234-1234-123456789abc+0x42\n" +
            "    IMAGE_NAME:  nvlddmkm.sys\n";

        var redacted = ReportRedaction.RedactCdbSummary(input);

        Assert.DoesNotContain("GPU-12345678-1234-1234-1234-123456789abc", redacted);
        Assert.Contains("[redacted]", redacted);
        Assert.Contains("nvlddmkm.sys", redacted);
        Assert.Contains("STACK_TEXT (top frames):", redacted);
    }

    [Fact]
    public void RedactMachineName_WordBoundary_DoesNotScrubSubstringMatches()
    {
        // A machine named "DEV" must not blank every occurrence of "DEVICE" or
        // "DEVELOPMENT" in the log — that's what the old substring replace did.
        var input = "Host DEV reported DEVICE removal in DEVELOPMENT build";

        var redacted = ReportRedaction.RedactMachineName(input, "DEV");

        Assert.Contains("[redacted]", redacted);
        Assert.Contains("DEVICE", redacted);
        Assert.Contains("DEVELOPMENT", redacted);
    }

    [Fact]
    public void RedactMachineName_CaseInsensitive_ReplacesAllCasings()
    {
        var input = "from MYHOST, myhost, and MyHost";

        var redacted = ReportRedaction.RedactMachineName(input, "MyHost");

        Assert.DoesNotContain("MYHOST", redacted);
        Assert.DoesNotContain("myhost", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(redacted, @"\[redacted\]").Count);
    }

    [Fact]
    public void RedactMachineName_TooShortName_LeavesInputUnchanged()
    {
        // 1-char names would match every standalone letter; refuse rather than
        // mangle ordinary prose. Pathologically short hostnames are rare.
        var input = "Press A to continue";

        var redacted = ReportRedaction.RedactMachineName(input, "A");

        Assert.Equal(input, redacted);
    }

    [Fact]
    public void RedactMachineName_NameWithRegexSpecialChars_IsEscaped()
    {
        // NetBIOS names technically allow some punctuation; Regex.Escape keeps
        // a hostname like "host.local" from accidentally matching anything.
        var input = "machine host.local talking to hostXlocal";

        var redacted = ReportRedaction.RedactMachineName(input, "host.local");

        Assert.Contains("[redacted]", redacted);
        Assert.Contains("hostXlocal", redacted);
    }

    [Fact]
    public void ScrubUserPaths_HandlesForwardSlashAndMixedCase()
    {
        var input =
            "c:/Users/Bob/dump.dmp and C:\\USERS\\CAROL\\file.sys";

        var scrubbed = ReportRedaction.ScrubUserPaths(input);

        Assert.DoesNotContain("Bob", scrubbed);
        Assert.DoesNotContain("CAROL", scrubbed);
        Assert.Contains(@"%USERPROFILE%/dump.dmp", scrubbed);
        Assert.Contains(@"%USERPROFILE%\file.sys", scrubbed);
    }

    [Fact]
    public void ScrubUserPaths_HandlesProfileNamesWithSpaces()
    {
        var input =
            @"SYMBOL_PATH: C:\Users\Alice Smith\symbols;C:\Users\O'Connor\AppData;C:\Windows\System32";

        var scrubbed = ReportRedaction.ScrubUserPaths(input);

        Assert.DoesNotContain("Alice", scrubbed);
        Assert.DoesNotContain("Smith", scrubbed);
        Assert.DoesNotContain("Connor", scrubbed);
        Assert.Contains(@"%USERPROFILE%\symbols", scrubbed);
        Assert.Contains(@"%USERPROFILE%\AppData", scrubbed);
        Assert.Contains(@"C:\Windows\System32", scrubbed);
    }

    [Fact]
    public void ScrubUserPaths_EnvBackedNonstandardUserProfile_IsScrubbed()
    {
        var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["USERPROFILE"] = @"D:\Profiles\Alice Smith",
        };
        var input =
            @"SYMBOL_PATH: D:\Profiles\Alice Smith\AppData\Local\Temp\dump.dmp;D:\Tools\keep.dll";

        var scrubbed = ReportRedaction.ScrubUserPaths(
            input,
            name => env.TryGetValue(name, out var value) ? value : null);

        Assert.DoesNotContain("Alice", scrubbed);
        Assert.DoesNotContain("Smith", scrubbed);
        Assert.Contains(@"%USERPROFILE%\AppData\Local\Temp\dump.dmp", scrubbed);
        Assert.Contains(@"D:\Tools\keep.dll", scrubbed);
    }

    [Fact]
    public void ScrubUserPaths_RedirectedAppDataEnvOutsideUserProfile_IsScrubbed()
    {
        var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["USERPROFILE"] = @"C:\Users\Alice",
            ["APPDATA"] = @"\\profiles\home$\Alice\AppData\Roaming",
        };
        var input =
            @"CONFIG: \\profiles\home$\Alice\AppData\Roaming\Vendor\settings.json";

        var scrubbed = ReportRedaction.ScrubUserPaths(
            input,
            name => env.TryGetValue(name, out var value) ? value : null);

        Assert.DoesNotContain("Alice", scrubbed);
        Assert.Contains(@"%APPDATA%\Vendor\settings.json", scrubbed);
    }

    [Fact]
    public void Generate_WithoutRedact_IncludesIdentifiers()
    {
        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, [])).Main;

        Assert.Contains("GPU-abc", report);
        Assert.Contains("0000:01:00.0", report);
    }

    [Fact]
    public void Generate_UnknownSmCount_OmitsProbabilityAndDenominator()
    {
        // When Vulkan SM-count probing fails (gpu.SmCount == 0) the probability
        // number and the "out of N total SMs" denominator must not appear in the
        // report — they would be computed from a guessed fallback and turn a
        // problem-surfacing tool into a conclusion.
        var gpu = TestGpu() with { SmCount = 0 };
        var errors = new List<NvlddmkmError>
        {
            new(new DateTime(2025, 1, 1), 13, "msg", 3, 1, 0, "Illegal Instruction Encoding"),
            new(new DateTime(2025, 1, 2), 13, "msg", 3, 1, 0, "Illegal Instruction Encoding"),
            new(new DateTime(2025, 1, 3), 13, "msg", 3, 1, 0, "Page Fault"),
        };

        var report = ReportGenerator.Generate(new ReportInput(gpu, null, errors)).Main;

        Assert.DoesNotContain("effectively zero", report);
        Assert.DoesNotContain("inconsistent with a random distribution", report);
        Assert.DoesNotContain("170", report);
        Assert.DoesNotContain("total SMs on the GPU", report);
        Assert.DoesNotContain("Total SMs on this GPU", report);
        Assert.Contains("SM count unavailable; concentration analysis omitted.", report);

        // Raw SM-location grouping must still appear — it does not depend on the denominator.
        Assert.Contains("Errors by SM location:", report);
        Assert.Contains("GPC 3, TPC 1, SM 0", report);
    }

    [Fact]
    public void Generate_UnknownSmCount_SummarySkipsDenominator()
    {
        var gpu = TestGpu() with { SmCount = 0 };
        var errors = new List<NvlddmkmError>
        {
            new(new DateTime(2025, 1, 1), 13, "msg", 3, 1, 0, "Illegal Instruction Encoding"),
            new(new DateTime(2025, 1, 2), 13, "msg", 3, 1, 0, "Illegal Instruction Encoding"),
        };

        var report = ReportGenerator.Generate(new ReportInput(gpu, null, errors)).Main;

        Assert.DoesNotContain("out of 170 total SMs", report);
        Assert.DoesNotContain("out of", report);
        Assert.Contains("specific SM location(s)", report);
    }

    [Fact]
    public void Generate_KnownSmCount_StillShowsConcentrationAnalysis()
    {
        // Regression guard: the SmCount > 0 path names the denominator and describes
        // concentration qualitatively. The old "10^X (effectively zero)" math is gone.
        var errors = new List<NvlddmkmError>();
        for (int i = 0; i < 12; i++)
            errors.Add(new(new DateTime(2025, 1, 1).AddDays(i), 13, "msg", 3, 1, 0, "Page Fault"));

        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, errors)).Main;

        Assert.Contains("Total SMs on this GPU: 128", report);
        Assert.Contains("1 of 128 SM(s) (0.8% of the GPU)", report);
        Assert.Contains("tight recurring cluster", report);
        Assert.Contains("troubleshooting lead", report);
        Assert.Contains("out of 128 total SMs", report);
        Assert.DoesNotContain("effectively zero", report);
        Assert.DoesNotContain("10^", report);
    }

    [Fact]
    public void Generate_FrequencyChartWithoutDrivers_ShowsNoMatchNote()
    {
        // When the parser returns zero driver installs but errors exist, the chart
        // otherwise drops its annotations silently. The note makes "parser caught
        // nothing / vendor format shifted / no installs in window" visible.
        var errors = new List<NvlddmkmError>
        {
            new(new DateTime(2025, 1, 1), 13, "msg", 3, 1, 0, "Page Fault"),
            new(new DateTime(2025, 1, 8), 13, "msg", 3, 1, 0, "Page Fault"),
        };

        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, errors)).Main;

        Assert.Contains("ERROR FREQUENCY", report);
        Assert.Contains("No driver install events matched", report);
    }

    [Fact]
    public void Generate_FrequencyChartWithDrivers_OmitsNoMatchNote()
    {
        var errors = new List<NvlddmkmError>
        {
            new(new DateTime(2025, 1, 1), 13, "msg", 3, 1, 0, "Page Fault"),
            new(new DateTime(2025, 1, 8), 13, "msg", 3, 1, 0, "Page Fault"),
        };
        var drivers = new List<EventLogParser.DriverInstallEvent>
        {
            new(new DateTime(2024, 12, 1), "32.0.15.8129", "setupapi: 32.0.15.8129"),
        };

        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, errors, null, null, drivers)).Main;

        Assert.Contains("ERROR FREQUENCY", report);
        Assert.DoesNotContain("No driver install events matched", report);
    }

    [Fact]
    public void Generate_FrequencyChart_DriverAnnotation_CollapsesConsecutiveDuplicatesPreservesReinstalls()
    {
        var errors = new List<NvlddmkmError>
        {
            new(new DateTime(2026, 5, 12, 10, 0, 0), 13, "msg", 3, 1, 0, "Page Fault"),
            new(new DateTime(2026, 5, 14, 10, 0, 0), 13, "msg", 3, 1, 0, "Page Fault"),
        };
        var drivers = new List<EventLogParser.DriverInstallEvent>
        {
            new(new DateTime(2026, 5, 10, 12, 39, 24), "32.0.15.9636", "setupapi: 596.36"),
            new(new DateTime(2026, 5, 12, 18, 21, 44), "32.0.15.9649", "setupapi: 596.49"),
            new(new DateTime(2026, 5, 12, 20, 44, 37), "32.0.15.9649", "setupapi: 596.49"),
            new(new DateTime(2026, 5, 12, 22, 15, 40), "32.0.15.9649", "setupapi: 596.49"),
            new(new DateTime(2026, 5, 14, 11, 42, 23), "32.0.15.8097", "setupapi: 580.97"),
            new(new DateTime(2026, 5, 14, 19, 50, 42), "32.0.15.9649", "setupapi: 596.49"),
            new(new DateTime(2026, 5, 14, 22,  0, 33), "32.0.15.9649", "setupapi: 596.49"),
        };

        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, errors, null, null, drivers)).Main;

        Assert.Contains("(drv 596.49 > 580.97 > 596.49)", report);
        Assert.DoesNotContain("596.49 > 596.49", report);
    }

    [Fact]
    public void ComputeDriverPeriodBuckets_NoDrivers_ReturnsEmpty()
    {
        var buckets = ReportGenerator.ComputeDriverPeriodBuckets([], []);
        Assert.Empty(buckets);
    }

    [Fact]
    public void ComputeDriverPeriodBuckets_SplitsErrorsAcrossInstalls()
    {
        var errors = new List<NvlddmkmError>
        {
            new(new DateTime(2025, 1, 1), 13, "msg", 0, 0, 0, null),   // pre-log
            new(new DateTime(2025, 2, 10), 13, "msg", 0, 0, 0, null),  // driver A window
            new(new DateTime(2025, 2, 20), 13, "msg", 0, 0, 0, null),  // driver A window
            new(new DateTime(2025, 3, 5), 13, "msg", 0, 0, 0, null),   // driver B window (open-ended)
        };
        var drivers = new List<EventLogParser.DriverInstallEvent>
        {
            new(new DateTime(2025, 2, 1), "32.0.15.8129", "a"),
            new(new DateTime(2025, 3, 1), "32.0.15.9999", "b"),
        };

        var buckets = ReportGenerator.ComputeDriverPeriodBuckets(errors, drivers);

        Assert.Equal(3, buckets.Count);
        Assert.True(buckets[0].IsPreLog);
        Assert.Equal(1, buckets[0].ErrorCount);
        Assert.Equal("32.0.15.8129", buckets[1].Version);
        Assert.Equal(2, buckets[1].ErrorCount);
        Assert.Equal("32.0.15.9999", buckets[2].Version);
        Assert.Null(buckets[2].End); // final bucket is open-ended
        Assert.Equal(1, buckets[2].ErrorCount);
    }

    [Fact]
    public void ComputeDriverPeriodBuckets_NoPreLogErrors_OmitsPreLogBucket()
    {
        var errors = new List<NvlddmkmError>
        {
            new(new DateTime(2025, 2, 10), 13, "msg", 0, 0, 0, null),
        };
        var drivers = new List<EventLogParser.DriverInstallEvent>
        {
            new(new DateTime(2025, 2, 1), "32.0.15.8129", "a"),
        };

        var buckets = ReportGenerator.ComputeDriverPeriodBuckets(errors, drivers);

        Assert.Single(buckets);
        Assert.False(buckets[0].IsPreLog);
    }

    [Fact]
    public void Generate_SwedishCulture_PercentageUsesDot()
    {
        // F-format specifiers must produce locale-stable output (dot, not comma)
        // so reports are consistent regardless of the machine's locale.
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("sv-SE");
            var errors = new List<NvlddmkmError>
            {
                new(new DateTime(2025, 1, 1), 13, "msg", 3, 1, 0, "Page Fault"),
                new(new DateTime(2025, 1, 2), 13, "msg", 3, 1, 0, "Page Fault"),
                new(new DateTime(2025, 1, 3), 13, "msg", 3, 1, 1, "Page Fault"),
            };
            var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, errors)).Main;

            // Two locations, 3 errors total. Percentages come out to 66.7% / 33.3%.
            Assert.Contains("66.7%", report);
            Assert.DoesNotContain("66,7%", report);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Generate_NullHealth_OmitsScopeBlock()
    {
        var errors = new List<NvlddmkmError>
        {
            new(new DateTime(2025, 1, 1), 13, "msg", 3, 1, 0, "Page Fault"),
        };
        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, errors)).Main;

        Assert.DoesNotContain("SCOPE OF THIS REPORT", report);
        Assert.DoesNotContain("see SCOPE block", report);
    }

    [Fact]
    public void Generate_HealthWithRequestedWindow_AlwaysEmitsScanWindow()
    {
        // A clean report without this line is indistinguishable between a 1-day
        // and a 10-year scan — which changes what a reader should infer from the report.
        var errors = new List<NvlddmkmError>
        {
            new(new DateTime(2025, 1, 1), 13, "msg", 3, 1, 0, "Page Fault"),
        };
        var report = ReportGenerator.Generate(new ReportInput(
            TestGpu(), null, errors,
            Health: new CollectorHealth
            {
                Truncation = new CollectionTruncation { RequestedMaxDays = 365, MaxEventsCap = 5000 },
            })).Main;

        Assert.Contains("SCOPE OF THIS REPORT", report);
        Assert.Contains("Requested window: last 365 day(s).", report);
        Assert.DoesNotContain("Capped sources:", report);
        Assert.DoesNotContain("Collector health:", report);
    }

    [Fact]
    public void Generate_HealthWithSystemEventLogRetention_EmitsRetainedRange()
    {
        var health = new CollectorHealth
        {
            Truncation = new CollectionTruncation { RequestedMaxDays = 3650 },
            SystemEventLog = new EventLogRetentionInfo(
                "System",
                "Circular",
                MaximumSizeInBytes: 268435456,
                FileSizeBytes: 134217728,
                RecordCount: 41965,
                OldestRecordNumber: 35005,
                OldestRecordTimestamp: new DateTime(2025, 12, 4, 13, 40, 33),
                OldestRelevantEventTimestamp: new DateTime(2025, 12, 12, 9, 50, 23),
                OldestRelevantEventDescription: "nvlddmkm 13/14/153"),
        };

        var report = ReportGenerator.Generate(new ReportInput(
            TestGpu(), null, [], Health: health)).Main;

        Assert.Contains("System Event Log retention:", report);
        Assert.Contains("System log: mode Circular; max 256.0 MiB; current 128.0 MiB; 41,965 record(s).", report);
        Assert.Contains("Oldest retained System record: 2025-12-04 13:40:33.", report);
        Assert.Contains("Oldest retained nvlddmkm 13/14/153 record: 2025-12-12 09:50:23.", report);
        Assert.Contains("Absence before that timestamp is not evidence that no GPU errors occurred.", report);
    }

    [Fact]
    public void Generate_HealthWithApplicationEventLogRetention_EmitsRetainedRange()
    {
        var health = new CollectorHealth
        {
            Truncation = new CollectionTruncation { RequestedMaxDays = 3650 },
            ApplicationEventLog = new EventLogRetentionInfo(
                "Application",
                "Circular",
                MaximumSizeInBytes: 134217728,
                FileSizeBytes: 67108864,
                RecordCount: 12345,
                OldestRecordNumber: 9123,
                OldestRecordTimestamp: new DateTime(2025, 11, 2, 8, 15, 0),
                OldestRelevantEventTimestamp: new DateTime(2025, 11, 4, 17, 22, 10),
                OldestRelevantEventDescription: "Application Error 1000 / Application Hang 1002"),
        };

        var report = ReportGenerator.Generate(new ReportInput(
            TestGpu(), null, [], Health: health)).Main;

        Assert.Contains("Application Event Log retention:", report);
        Assert.Contains("Application log: mode Circular; max 128.0 MiB; current 64.0 MiB; 12,345 record(s).", report);
        Assert.Contains("Oldest retained Application record: 2025-11-02 08:15:00.", report);
        Assert.Contains("Oldest retained Application Error 1000 / Application Hang 1002 record: 2025-11-04 17:22:10.", report);
        Assert.Contains("Absence before that timestamp is not evidence that no application crash/hang events occurred.", report);
    }

    [Fact]
    public void Generate_EmptyHealth_OmitsScopeBlock()
    {
        var errors = new List<NvlddmkmError>
        {
            new(new DateTime(2025, 1, 1), 13, "msg", 3, 1, 0, "Page Fault"),
        };
        var report = ReportGenerator.Generate(new ReportInput(
            TestGpu(), null, errors, Health: new CollectorHealth())).Main;

        Assert.DoesNotContain("SCOPE OF THIS REPORT", report);
        Assert.DoesNotContain("see SCOPE block", report);
    }

    [Fact]
    public void Generate_GpuErrorsCapped_EmitsScopeBlockAndSummarySuffix()
    {
        var errors = new List<NvlddmkmError>
        {
            new(new DateTime(2025, 1, 1), 13, "msg", 3, 1, 0, "Page Fault"),
            new(new DateTime(2025, 1, 2), 13, "msg", 3, 1, 0, "Page Fault"),
        };
        var health = new CollectorHealth
        {
            Truncation = new CollectionTruncation
            {
                RequestedMaxDays = 365,
                MaxEventsCap = 5000,
                GpuErrorsResultCap = true,
            },
        };

        var report = ReportGenerator.Generate(new ReportInput(
            TestGpu(), null, errors, Health: health)).Main;

        Assert.Contains("SCOPE OF THIS REPORT", report);
        Assert.Contains("Requested window: last 365 day(s).", report);
        Assert.Contains("nvlddmkm errors: Max Events cap (5000) reached", report);
        Assert.Contains("(capped — see SCOPE block)", report);
        Assert.Contains("Collected range:", report);
        Assert.DoesNotContain("Date range:", report);
    }

    [Fact]
    public void Generate_GpuErrorsCapped_SummaryBlockAnnotatesLowerBound()
    {
        var errors = new List<NvlddmkmError>
        {
            new(new DateTime(2025, 1, 1), 13, "msg", 3, 1, 0, "Page Fault"),
            new(new DateTime(2025, 1, 2), 13, "msg", 3, 1, 0, "Page Fault"),
        };
        var health = new CollectorHealth
        {
            Truncation = new CollectionTruncation
            {
                RequestedMaxDays = 365,
                MaxEventsCap = 5000,
                GpuErrorsResultCap = true,
            },
        };

        var report = ReportGenerator.Generate(new ReportInput(
            TestGpu(), null, errors, Health: health)).Main;

        var summaryBlock = SliceFinalSummary(report);
        Assert.Contains("2 GPU errors recorded in Windows Event Log. (capped — see SCOPE block)", summaryBlock);
    }

    [Fact]
    public void Generate_GpuErrorsNotCapped_SummaryBlockHasNoCapSuffix()
    {
        var errors = new List<NvlddmkmError>
        {
            new(new DateTime(2025, 1, 1), 13, "msg", 3, 1, 0, "Page Fault"),
            new(new DateTime(2025, 1, 2), 13, "msg", 3, 1, 0, "Page Fault"),
        };

        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, errors)).Main;

        var summaryBlock = SliceFinalSummary(report);
        Assert.Contains("2 GPU errors recorded in Windows Event Log.", summaryBlock);
        Assert.DoesNotContain("capped", summaryBlock);
    }

    private static string SliceFinalSummary(string report)
    {
        var idx = report.LastIndexOf("## SUMMARY", StringComparison.Ordinal);
        Assert.True(idx >= 0, "final SUMMARY section header expected");
        return report[idx..];
    }

    [Fact]
    public void Generate_GpuErrorsCapped_AnnotatesAppCorrelationButNotAppCrashList()
    {
        var baseTime = new DateTime(2025, 1, 15, 10, 0, 0);
        var errors = new List<NvlddmkmError>
        {
            new(baseTime, 13, "msg", 3, 1, 0, "Page Fault"),
        };
        var appCrashes = new List<EventLogParser.AppCrashEvent>
        {
            new(baseTime.AddSeconds(5), "game.exe", "nvlddmkm.sys", "game.exe crashed"),
        };
        var health = new CollectorHealth
        {
            Truncation = new CollectionTruncation
            {
                RequestedMaxDays = 365,
                MaxEventsCap = 5000,
                GpuErrorsResultCap = true,
            },
        };

        var report = ReportGenerator.Generate(new ReportInput(
            TestGpu(), null, errors, null, appCrashes, Health: health)).Main;

        Assert.Contains("SCOPE OF THIS REPORT", report);
        Assert.Contains("nvlddmkm errors: Max Events cap (5000) reached.", report);
        Assert.Contains("matched counts are a lower", report);
        Assert.Contains("APPLICATION CRASHES (1 total)", report);
        Assert.DoesNotContain("collected, capped", report);
        Assert.DoesNotContain("Application Error / Hang event reads hit their caps", report);
    }

    [Fact]
    public void Generate_RebootCapped_AnnotatesCorrelationLine()
    {
        var ts = new DateTime(2025, 1, 15, 10, 0, 0);
        var errors = new List<NvlddmkmError>
        {
            new(ts, 13, "msg", 3, 1, 0, "Page Fault"),
        };
        var crashes = new List<SystemCrashEvent>
        {
            new(ts.AddMinutes(2), "REBOOT", 41, "Unexpected reboot: VIDEO_TDR_FAILURE (code 0x00000116)"),
        };
        var health = new CollectorHealth
        {
            Truncation = new CollectionTruncation
            {
                RequestedMaxDays = 365,
                MaxEventsCap = 5000,
                RebootResultCap = true,
            },
        };

        var report = ReportGenerator.Generate(new ReportInput(
            TestGpu(), null, errors, crashes, Health: health)).Main;

        Assert.Contains("SCOPE OF THIS REPORT", report);
        Assert.Contains("Unexpected reboots (Kernel-Power 41): 200 cap reached.", report);
        Assert.Contains("the matched count is a", report);
        Assert.Contains("lower bound", report);
        Assert.Contains("(capped — see SCOPE block)", report);
    }

    [Fact]
    public void Generate_DriverInstallHistory_GpuErrorsCapped_UsesGpuLowerBoundNoteOnly()
    {
        var errors = new List<NvlddmkmError>
        {
            new(new DateTime(2025, 2, 10), 13, "msg", 0, 0, 0, null),
        };
        var drivers = new List<EventLogParser.DriverInstallEvent>
        {
            new(new DateTime(2025, 2, 1), "32.0.15.8129", "a"),
        };
        var health = new CollectorHealth
        {
            Truncation = new CollectionTruncation
            {
                RequestedMaxDays = 365,
                MaxEventsCap = 5000,
                GpuErrorsResultCap = true,
            },
        };

        var report = ReportGenerator.Generate(new ReportInput(
            TestGpu(), null, errors, null, null, drivers, Health: health)).Main;

        Assert.Contains("SCOPE OF THIS REPORT", report);
        Assert.Contains("nvlddmkm errors: Max Events cap (5000) reached.", report);
        Assert.Contains("nvlddmkm events were capped; per-period error counts are a lower", report);
        Assert.DoesNotContain("driver install scan cap was reached before matching completed", report);
        Assert.DoesNotContain("Driver installs (Kernel-PnP)", report);
    }

    [Fact]
    public void Generate_HealthWithCanary_RendersInScopeBlock()
    {
        var health = new CollectorHealth();
        health.Canary("nvlddmkm classifier", "47 events but none matched known payload shapes");

        var report = ReportGenerator.Generate(new ReportInput(
            TestGpu(), null, [], Health: health)).Main;

        Assert.Contains("SCOPE OF THIS REPORT", report);
        Assert.Contains("Collector health:", report);
        Assert.Contains("[format-drift] nvlddmkm classifier: 47 events but none matched known payload shapes", report);
    }

    [Fact]
    public void Generate_LongHealthNotice_EmitsSingleBulletLine()
    {
        var health = new CollectorHealth();
        health.Canary(
            "DeviceSetupManager driver install version scheme",
            "63 DeviceSetupManager driver install event(s) matched the NVIDIA driver-install filter but did not include a driver version FLARE could use (15 matched and were parsed). This affects only Driver Install History; it may be incomplete.");

        var report = ReportGenerator.Generate(new ReportInput(
            TestGpu(), null, [], Health: health)).Main;
        var normalized = report.ReplaceLineEndings("\n");

        Assert.Contains(
            "- [format-drift] DeviceSetupManager driver install version scheme: 63 DeviceSetupManager driver install event(s) matched the NVIDIA driver-install filter but did not include a driver version FLARE could use (15 matched and were parsed). This affects only Driver Install History; it may be incomplete.",
            normalized);
    }

    [Fact]
    public void Generate_HealthWithFailure_RendersInScopeBlock()
    {
        var health = new CollectorHealth();
        health.Failure("nvidia-smi", "not found at System32\\nvidia-smi.exe");

        var report = ReportGenerator.Generate(new ReportInput(
            TestGpu(), null, [], Health: health)).Main;

        Assert.Contains("SCOPE OF THIS REPORT", report);
        Assert.Contains("Collector health:", report);
        Assert.Contains("[failed] nvidia-smi: not found at System32\\nvidia-smi.exe", report);
    }

    [Fact]
    public void Generate_HealthWithSkipped_RendersInScopeBlock()
    {
        var health = new CollectorHealth();
        health.Skipped("minidump copy", "disabled by user");

        var report = ReportGenerator.Generate(new ReportInput(
            TestGpu(), null, [], Health: health)).Main;

        Assert.Contains("Collector health:", report);
        Assert.Contains("[skipped] minidump copy: disabled by user", report);
    }

    [Fact]
    public void Generate_GpuEventLogFailure_DoesNotClaimNoErrorsFound()
    {
        var health = new CollectorHealth
        {
            Truncation = new CollectionTruncation { RequestedMaxDays = 365 },
        };
        health.Failure("Event Log: nvlddmkm", "access denied");

        var report = ReportGenerator.Generate(new ReportInput(
            TestGpu(), null, [], Health: health)).Main;

        Assert.Contains("[failed] Event Log: nvlddmkm: access denied", report);
        Assert.Contains("No nvlddmkm errors were collected because the Event Log collector failed", report);
        Assert.Contains("nvlddmkm Event Log collection failed; no conclusion can be drawn", report);
        Assert.DoesNotContain("No nvlddmkm errors found in Windows Event Log.", report);
        Assert.DoesNotContain("No errors found in Event Log at this time.", report);
    }

    [Fact]
    public void Generate_RedactEnabled_ScrubsUserPathsInNoticeMessages()
    {
        var health = new CollectorHealth();
        health.Failure("settings", @"Access to the path 'C:\Users\alice\AppData\Local\FLARE\settings.json' is denied.");

        var report = ReportGenerator.Generate(new ReportInput(
            TestGpu(), null, [], Health: health, RedactIdentifiers: true)).Main;

        Assert.DoesNotContain("alice", report);
        Assert.Contains(@"%USERPROFILE%\AppData\Local\FLARE\settings.json", report);
        Assert.Contains("[failed] settings:", report);
    }

    [Fact]
    public void Generate_RedactEnabled_ScrubsMachineNameInNoticeMessages()
    {
        var health = new CollectorHealth();
        health.Failure("BIOS registry", $"denied on {Environment.MachineName}\\HARDWARE");

        var report = ReportGenerator.Generate(new ReportInput(
            TestGpu(), null, [], Health: health, RedactIdentifiers: true)).Main;

        if (Environment.MachineName.Length >= 2)
            Assert.DoesNotContain(Environment.MachineName, report, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Generate_RedactDisabled_LeavesNoticeMessagesIntact()
    {
        var health = new CollectorHealth();
        health.Failure("settings", @"Access to 'C:\Users\alice\file' is denied.");

        var report = ReportGenerator.Generate(new ReportInput(
            TestGpu(), null, [], Health: health, RedactIdentifiers: false)).Main;

        Assert.Contains("alice", report);
    }

    [Fact]
    public void Generate_GpuErrorsCapped_FrequencyChartEmitsInlineCapNote()
    {
        var errors = new List<NvlddmkmError>
        {
            new(new DateTime(2025, 1, 1),  13, "msg", 3, 1, 0, "Page Fault"),
            new(new DateTime(2025, 1, 8),  13, "msg", 3, 1, 0, "Page Fault"),
        };
        var health = new CollectorHealth
        {
            Truncation = new CollectionTruncation
            {
                RequestedMaxDays = 365,
                MaxEventsCap = 5000,
                GpuErrorsResultCap = true,
            },
        };

        var report = ReportGenerator.Generate(new ReportInput(
            TestGpu(), null, errors, Health: health)).Main;

        var chartStart = report.IndexOf("## ERROR FREQUENCY", StringComparison.Ordinal);
        var chartEnd = report.IndexOf(". ", chartStart + 1, StringComparison.Ordinal);
        Assert.True(chartStart >= 0 && chartEnd > chartStart, "frequency chart section expected");
        var chart = report[chartStart..chartEnd];
        Assert.Contains("weekly counts are a lower", chart);
    }

    [Fact]
    public void Generate_GpuErrorsNotCapped_FrequencyChartOmitsInlineCapNote()
    {
        var errors = new List<NvlddmkmError>
        {
            new(new DateTime(2025, 1, 1),  13, "msg", 3, 1, 0, "Page Fault"),
            new(new DateTime(2025, 1, 8),  13, "msg", 3, 1, 0, "Page Fault"),
        };

        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, errors)).Main;

        Assert.Contains("ERROR FREQUENCY", report);
        Assert.DoesNotContain("weekly counts are a lower", report);
    }

    [Fact]
    public void Generate_HealthWithBothCanaryAndFailure_RendersBoth()
    {
        var health = new CollectorHealth
        {
            Truncation = new CollectionTruncation { RequestedMaxDays = 30 },
        };
        health.Failure("BIOS registry", "access denied");
        health.Canary("cdb summary extractor", "banner seen but no tag lines matched");

        var report = ReportGenerator.Generate(new ReportInput(
            TestGpu(), null, [], Health: health)).Main;

        Assert.Contains("Requested window: last 30 day(s).", report);
        Assert.Contains("Collector health:", report);
        Assert.Contains("[failed] BIOS registry: access denied", report);
        Assert.Contains("[format-drift] cdb summary extractor:", report);
    }

    [Fact]
    public void Generate_AllAppCrashes_AnnotatesAsUnfiltered()
    {
        var appCrashes = new List<EventLogParser.AppCrashEvent>
        {
            new(new DateTime(2025, 1, 1), "chrome.exe", "ntdll.dll", "chrome.exe crash"),
        };

        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, [], null, appCrashes)).Main;

        Assert.Contains("APPLICATION CRASHES", report);
        Assert.Contains("not just GPU-related", report);
        Assert.Contains("correlation", report);
    }

    [Fact]
    public void Generate_AppCrashCorrelation_CarriesCoincidenceHedge()
    {
        var ts = new DateTime(2025, 1, 15, 10, 0, 0);
        var errors = new List<NvlddmkmError>
        {
            new(ts, 13, "msg", 3, 1, 0, "Page Fault"),
        };
        var appCrashes = new List<EventLogParser.AppCrashEvent>
        {
            new(ts.AddSeconds(5), "game.exe", "nvlddmkm.sys", "game.exe crashed"),
        };

        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, errors, null, appCrashes)).Main;

        Assert.Contains("timing hint, not a cause", report);
        Assert.Contains("30-second window", report);
    }

    [Fact]
    public void Generate_DriverInstallHistory_GpuErrorsCapped_MarksBlindPeriodsAsTruncatedOut()
    {
        var earliestCollected = new DateTime(2025, 2, 10);
        var errors = new List<NvlddmkmError>
        {
            new(earliestCollected,                13, "m", 3, 1, 0, "Page Fault"),
            new(earliestCollected.AddDays(10),    13, "m", 3, 1, 0, "Page Fault"),
        };
        var drivers = new List<EventLogParser.DriverInstallEvent>
        {
            new(new DateTime(2024, 6,  1), "31.0.15.2802", "d1"),
            new(new DateTime(2024, 12, 1), "31.0.15.5599", "d2"),
            new(new DateTime(2025, 1,  1), "32.0.15.8129", "d3"),
        };
        var health = new CollectorHealth
        {
            Truncation = new CollectionTruncation
            {
                RequestedMaxDays = 365,
                MaxEventsCap = 5000,
                GpuErrorsResultCap = true,
            },
        };

        var report = ReportGenerator.Generate(new ReportInput(
            TestGpu(), null, errors, DriverInstalls: drivers, Health: health, SortDescending: false)).Main;

        Assert.Contains("528.02   truncated-out  (to 2024-12-01)", report);
        Assert.Contains("555.99   truncated-out  (to 2025-01-01)", report);
        Assert.Contains("581.29       2 errors  (partial; before 2025-02-10 not collected, to present)", report);
    }

    [Fact]
    public void Generate_DriverInstallHistory_NotCapped_RendersPlainZeros()
    {
        var errors = new List<NvlddmkmError>
        {
            new(new DateTime(2025, 2, 10), 13, "m", 3, 1, 0, "Page Fault"),
        };
        var drivers = new List<EventLogParser.DriverInstallEvent>
        {
            new(new DateTime(2024, 6, 1), "31.0.15.2802", "d1"),
            new(new DateTime(2025, 1, 1), "32.0.15.8129", "d3"),
        };

        var report = ReportGenerator.Generate(new ReportInput(
            TestGpu(), null, errors, DriverInstalls: drivers, SortDescending: false)).Main;

        Assert.Contains("528.02       0 errors", report);
        Assert.DoesNotContain("truncated-out", report);
    }

    [Fact]
    public void Generate_DriverInstallHistory_SystemRetentionFloor_MarksOldPeriodsAsUnobservable()
    {
        var errors = new List<NvlddmkmError>
        {
            new(new DateTime(2025, 12, 5), 13, "m", 3, 1, 0, "Page Fault"),
            new(new DateTime(2025, 12, 15), 13, "m", 3, 1, 0, "Page Fault"),
        };
        var drivers = new List<EventLogParser.DriverInstallEvent>
        {
            new(new DateTime(2025, 10, 14, 21, 5, 33), "32.0.15.8157", "d1"),
            new(new DateTime(2025, 11, 5, 8, 3, 6), "32.0.15.8180", "d2"),
            new(new DateTime(2025, 12, 4, 20, 38, 9), "32.0.15.9144", "d3"),
            new(new DateTime(2025, 12, 12, 16, 47, 9), "32.0.15.9144", "d4"),
        };
        var health = new CollectorHealth
        {
            Truncation = new CollectionTruncation { RequestedMaxDays = 365 },
            SystemEventLog = new EventLogRetentionInfo(
                "System",
                "Circular",
                MaximumSizeInBytes: 268435456,
                FileSizeBytes: null,
                RecordCount: null,
                OldestRecordNumber: null,
                OldestRecordTimestamp: new DateTime(2025, 12, 4, 13, 40, 33),
                OldestRelevantEventTimestamp: new DateTime(2025, 12, 12, 9, 50, 23),
                OldestRelevantEventDescription: "nvlddmkm 13/14/153"),
        };

        var report = ReportGenerator.Generate(new ReportInput(
            TestGpu(), null, errors, DriverInstalls: drivers, Health: health, SortDescending: false)).Main;

        Assert.Contains("Periods before 2025-12-04 13:40:33 cannot prove zero nvlddmkm errors.", report);
        Assert.Contains("2025-10-14  581.57   not retained (to 2025-11-05)", report);
        Assert.Contains("2025-11-05  581.80       0 errors  (partial; before 2025-12-04 not retained, to 2025-12-04)", report);
        Assert.Contains("2025-12-04  591.44       1 errors  (to 2025-12-12)", report);
        Assert.DoesNotContain("2025-10-14  581.57       0 errors", report);
    }

    [Fact]
    public void Generate_DriverInstallHistory_PreLogBucketWithinRequestedWindow_OmitsNotRetainedAnnotation()
    {
        var errors = new List<NvlddmkmError>
        {
            new(new DateTime(2026, 5, 11, 9, 0, 0),  13, "m", 3, 1, 0, "Page Fault"),
            new(new DateTime(2026, 5, 11, 10, 0, 0), 13, "m", 3, 1, 0, "Page Fault"),
        };
        var drivers = new List<EventLogParser.DriverInstallEvent>
        {
            new(new DateTime(2026, 5, 12, 8, 0, 0), "32.0.15.9144", "d1"),
        };
        var now = new DateTime(2026, 5, 14, 12, 0, 0);
        var health = new CollectorHealth
        {
            Truncation = new CollectionTruncation { RequestedMaxDays = 4 },
            SystemEventLog = new EventLogRetentionInfo(
                "System",
                "Circular",
                MaximumSizeInBytes: 268435456,
                FileSizeBytes: null,
                RecordCount: null,
                OldestRecordNumber: null,
                OldestRecordTimestamp: new DateTime(2025, 12, 4, 13, 40, 33),
                OldestRelevantEventTimestamp: new DateTime(2025, 12, 12, 9, 50, 23),
                OldestRelevantEventDescription: "nvlddmkm 13/14/153"),
        };

        var buckets = ReportGenerator.ComputeDriverPeriodBuckets(
            errors, drivers, requestedWindowStart: now.AddDays(-health.Truncation.RequestedMaxDays));

        Assert.Equal(2, buckets.Count);
        Assert.True(buckets[0].IsPreLog);
        Assert.Equal(2, buckets[0].ErrorCount);
        Assert.True(buckets[0].Start >= now.AddDays(-health.Truncation.RequestedMaxDays).AddSeconds(-1),
            $"pre-log Start should be clamped to requested window start, was {buckets[0].Start:O}");

        var report = ReportGenerator.Generate(new ReportInput(
            TestGpu(), null, errors, DriverInstalls: drivers, Health: health, SortDescending: false)).Main;

        var preLogLine = report.Split('\n').FirstOrDefault(l => l.Contains("(unknown, pre-log)")) ?? "";
        Assert.DoesNotContain("not retained", preLogLine);
        Assert.DoesNotContain("partial", preLogLine);
        Assert.Contains("2 errors", preLogLine);
    }

    [Fact]
    public void Generate_StrongConcentrationWithGpuErrorsCapped_AddsInlineCapCaveat()
    {
        var errors = new List<NvlddmkmError>();
        for (int i = 0; i < 12; i++)
            errors.Add(new(new DateTime(2025, 1, 1).AddDays(i), 13, "msg", 3, 1, 0, "Illegal Instruction Encoding"));

        var health = new CollectorHealth
        {
            Truncation = new CollectionTruncation
            {
                RequestedMaxDays = 365,
                MaxEventsCap = 5000,
                GpuErrorsResultCap = true,
            },
        };

        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, errors, Health: health)).Main;

        Assert.Contains("tight recurring cluster", report);
        Assert.Contains("Max Events cap was reached", report);
        Assert.Contains("the full history may include errors on other SMs", report);
        Assert.Contains("This reads the collected subset only", report);
    }

    [Fact]
    public void Generate_StrongConcentrationWithoutCap_DoesNotAddCapCaveat()
    {
        var errors = new List<NvlddmkmError>();
        for (int i = 0; i < 12; i++)
            errors.Add(new(new DateTime(2025, 1, 1).AddDays(i), 13, "msg", 3, 1, 0, "Illegal Instruction Encoding"));

        var report = ReportGenerator.Generate(new ReportInput(TestGpu(), null, errors)).Main;

        Assert.Contains("tight recurring cluster", report);
        Assert.DoesNotContain("Max Events cap was reached", report);
        Assert.DoesNotContain("This reads the collected subset only", report);
    }

    [Fact]
    public void Generate_RedactOn_FinalPassScrubsMachineNameInCrashDescriptions()
    {
        var machineName = Environment.MachineName;
        if (string.IsNullOrWhiteSpace(machineName) || machineName.Length < 2) return;

        var crashes = new List<SystemCrashEvent>
        {
            new(new DateTime(2025, 1, 1), "BSOD", 1001,
                $"Fault bucket report produced by {machineName} while running game.exe"),
        };

        var report = ReportGenerator.Generate(new ReportInput(
            TestGpu(), null, [], crashes, RedactIdentifiers: true)).Main;

        Assert.DoesNotContain(machineName, report);
        Assert.Contains("[redacted]", report);
    }

    [Fact]
    public void Generate_RedactOn_FinalPassScrubsUserPathsInAppCrashDescriptions()
    {
        var appCrashes = new List<EventLogParser.AppCrashEvent>
        {
            new(new DateTime(2025, 1, 1), @"C:\Users\alice\Desktop\game.exe", @"ntdll.dll",
                @"C:\Users\alice\Desktop\game.exe (faulting module: ntdll.dll)"),
        };

        var report = ReportGenerator.Generate(new ReportInput(
            TestGpu(), null, [], null, appCrashes, RedactIdentifiers: true)).Main;

        Assert.DoesNotContain(@"C:\Users\alice", report);
        Assert.Contains(@"%USERPROFILE%\Desktop\game.exe", report);
    }

    [Fact]
    public void ScrubUserPaths_BarePathAtEndOfString_IsScrubbed()
    {
        var input = @"DIR: C:\Users\Daniel";

        var scrubbed = ReportRedaction.ScrubUserPaths(input);

        Assert.DoesNotContain("Daniel", scrubbed);
        Assert.Contains(@"DIR: %USERPROFILE%", scrubbed);
    }

    [Fact]
    public void ScrubUserPaths_BarePathFollowedByQuote_IsScrubbed()
    {
        var input = "home=\"C:\\Users\\Daniel\" end";

        var scrubbed = ReportRedaction.ScrubUserPaths(input);

        Assert.DoesNotContain("Daniel", scrubbed);
        Assert.Contains("home=\"%USERPROFILE%\" end", scrubbed);
    }

    [Fact]
    public void ScrubUserPaths_BarePathWithSpacesFollowedByQuote_IsScrubbed()
    {
        var input = "home=\"C:\\Users\\Alice Smith\" end";

        var scrubbed = ReportRedaction.ScrubUserPaths(input, currentUserProfile: null);

        Assert.DoesNotContain("Alice", scrubbed);
        Assert.DoesNotContain("Smith", scrubbed);
        Assert.Contains("home=\"%USERPROFILE%\" end", scrubbed);
    }

    [Fact]
    public void ScrubUserPaths_SingleQuotedBarePathWithSpaces_IsScrubbed()
    {
        var input = "home='C:\\Users\\Alice Smith' end";

        var scrubbed = ReportRedaction.ScrubUserPaths(input, currentUserProfile: null);

        Assert.DoesNotContain("Alice", scrubbed);
        Assert.DoesNotContain("Smith", scrubbed);
        Assert.Contains("home='%USERPROFILE%' end", scrubbed);
    }

    [Fact]
    public void ScrubUserPaths_BarePathFollowedByWhitespace_DoesNotGobbleTrailingWord()
    {
        var input = @"see C:\Users\Daniel foo bar";

        var scrubbed = ReportRedaction.ScrubUserPaths(input);

        Assert.DoesNotContain("Daniel", scrubbed);
        Assert.Contains(@"see %USERPROFILE% foo bar", scrubbed);
    }

    [Fact]
    public void ScrubUserPaths_BarePathWithApostropheFollowedByWhitespace_IsScrubbed()
    {
        var input = @"see C:\Users\O'Connor foo bar";

        var scrubbed = ReportRedaction.ScrubUserPaths(input, currentUserProfile: null);

        Assert.DoesNotContain("Connor", scrubbed);
        Assert.Contains(@"see %USERPROFILE% foo bar", scrubbed);
    }

    [Fact]
    public void ScrubUserPaths_KnownBarePathWithSpacesFollowedByWhitespace_IsScrubbedWithoutGobblingTrailingWord()
    {
        var input = @"see C:\Users\Alice Smith foo bar";

        var scrubbed = ReportRedaction.ScrubUserPaths(input, @"C:\Users\Alice Smith");

        Assert.DoesNotContain("Alice", scrubbed);
        Assert.DoesNotContain("Smith", scrubbed);
        Assert.Contains(@"see %USERPROFILE% foo bar", scrubbed);
    }

    [Fact]
    public void Generate_GpuCollectorFailed_RendersPlaceholderInsteadOfEmptyFields()
    {
        var emptyGpu = new GpuInfo("", "", "", "", "", "", 0, "", 0, 0, 0, 0, 0, 1);
        var health = new CollectorHealth();
        health.Failure("nvidia-smi", "not found at System32\\nvidia-smi.exe");

        var report = ReportGenerator.Generate(new ReportInput(emptyGpu, null, [], Health: health)).Main;

        Assert.Contains("## GPU IDENTIFICATION", report);
        Assert.Contains("GPU identification unavailable", report);
        Assert.Contains("see SCOPE block", report);
        Assert.DoesNotContain("  GPU:            \n", report);
        Assert.DoesNotContain("  Driver:         \n", report);
    }

    [Fact]
    public void Generate_GpuCollectorFailed_SuppressesAllGpuFieldsAfterPlaceholder()
    {
        var gpuWithVulkanData = new GpuInfo("", "", "", "", "", "", 128, "24576 MB", 0, 0, 0, 0, 0, 1);
        var health = new CollectorHealth();
        health.Failure("nvidia-smi", "not found at System32\\nvidia-smi.exe");

        var report = ReportGenerator.Generate(new ReportInput(gpuWithVulkanData, null, [], Health: health)).Main;

        Assert.Contains("GPU identification unavailable", report);
        Assert.DoesNotContain("SMs:", report);
        Assert.DoesNotContain("24576 MB", report);
    }

    [Fact]
    public void Generate_GpuCollectorFailedWithoutHealth_DoesNotPointAtMissingScope()
    {
        var emptyGpu = new GpuInfo("", "", "", "", "", "", 0, "", 0, 0, 0, 0, 0, 1);

        var report = ReportGenerator.Generate(new ReportInput(emptyGpu, null, [])).Main;

        Assert.Contains("GPU identification unavailable.", report);
        Assert.DoesNotContain("see SCOPE block", report);
    }

    [Fact]
    public void Generate_AppCrashesCapped_EmitsScopeBlockLine()
    {
        var health = new CollectorHealth
        {
            Truncation = new CollectionTruncation
            {
                RequestedMaxDays = 365,
                MaxEventsCap = 5000,
                AppCrashesResultCap = true,
            },
        };

        var report = ReportGenerator.Generate(new ReportInput(
            TestGpu(), null, [], Health: health)).Main;

        Assert.Contains("SCOPE OF THIS REPORT", report);
        Assert.Contains($"Application crashes/hangs: {EventLogParser.AppCrashCap} cap reached.", report);
    }

    [Fact]
    public void Generate_MainReport_StartsWithH1Header()
    {
        var r = ReportGenerator.Generate(new ReportInput(
            Gpu: TestGpu(),
            System: null,
            Errors: new()));

        Assert.StartsWith("# GPU Error Analysis Report", r.Main);
    }

    [Fact]
    public void Generate_MainReport_FooterIsItalicBlockquote()
    {
        var r = ReportGenerator.Generate(new ReportInput(
            Gpu: TestGpu(),
            System: null,
            Errors: new()));

        Assert.Matches(@"> _Report generated by FLARE.*on \d{4}-\d{2}-\d{2}", r.Main);
    }

    [Fact]
    public void Generate_MainReport_RedactionNoteIsBlockquote()
    {
        var r = ReportGenerator.Generate(new ReportInput(
            Gpu: TestGpu(),
            System: null,
            Errors: new(),
            RedactIdentifiers: true));

        Assert.Contains("> **Note:**", r.Main);
    }

    [Fact]
    public void Generate_ScopeSection_UsesMarkdownHeader()
    {
        var health = new CollectorHealth();
        health.Truncation.RequestedMaxDays = 30;
        health.Truncation.MaxEventsCap = 5000;
        var r = ReportGenerator.Generate(new ReportInput(
            Gpu: TestGpu(),
            System: null,
            Errors: new(),
            Health: health));

        Assert.Contains("## SCOPE OF THIS REPORT", r.Main);
        Assert.Contains("Requested window: last 30 day(s).", r.Main);
    }

    [Fact]
    public void Generate_ScopeSection_NoticesUseMarkdownBullets()
    {
        var health = new CollectorHealth();
        health.Truncation.RequestedMaxDays = 30;
        health.Truncation.MaxEventsCap = 5000;
        health.Failure("Event Log: nvlddmkm", "test reason for failure");
        var r = ReportGenerator.Generate(new ReportInput(
            Gpu: TestGpu(),
            System: null,
            Errors: new(),
            Health: health));

        Assert.Contains("- [failed] Event Log: nvlddmkm: test reason for failure", r.Main);
    }

    [Fact]
    public void Generate_GpuIdentification_UsesMarkdownHeader()
    {
        var r = ReportGenerator.Generate(new ReportInput(
            Gpu: TestGpu(),
            System: null,
            Errors: new()));

        Assert.Contains("## GPU IDENTIFICATION", r.Main);
    }

    [Fact]
    public void Generate_GpuIdentification_WrappedInCodeFence()
    {
        var r = ReportGenerator.Generate(new ReportInput(
            Gpu: TestGpu(),
            System: null,
            Errors: new()));

        var gpuIdx = r.Main.IndexOf("## GPU IDENTIFICATION", StringComparison.Ordinal);
        Assert.True(gpuIdx > 0);
        var afterHeader = r.Main.Substring(gpuIdx);
        Assert.Contains("```", afterHeader);
    }

    [Fact]
    public void Generate_SystemIdentification_UsesMarkdownHeader()
    {
        var r = ReportGenerator.Generate(new ReportInput(
            Gpu: TestGpu(),
            System: TestSystem(),
            Errors: new()));

        Assert.Contains("## SYSTEM IDENTIFICATION", r.Main);
    }

    [Fact]
    public void Generate_NvlddmkmErrorSummary_UsesMarkdownHeader()
    {
        var r = ReportGenerator.Generate(new ReportInput(
            Gpu: TestGpu(),
            System: null,
            Errors: new()));

        Assert.Contains("## NVLDDMKM ERROR SUMMARY", r.Main);
    }

    [Fact]
    public void Generate_SummarySection_UsesMarkdownHeader()
    {
        var r = ReportGenerator.Generate(new ReportInput(
            Gpu: TestGpu(),
            System: null,
            Errors: new()));

        Assert.Contains("## SUMMARY", r.Main);
    }

    [Fact]
    public void Generate_FrequencyChart_UsesMarkdownHeaderAndCodeFence()
    {
        var errors = new List<NvlddmkmError>
        {
            new(new DateTime(2026, 5, 1, 0, 0, 0), 13, "", null, null, null, null),
            new(new DateTime(2026, 5, 8, 0, 0, 0), 13, "", null, null, null, null),
        };
        var r = ReportGenerator.Generate(new ReportInput(
            Gpu: TestGpu(),
            System: null,
            Errors: errors));

        Assert.Contains("## ERROR FREQUENCY (per week)", r.Main);
        var idx = r.Main.IndexOf("## ERROR FREQUENCY", StringComparison.Ordinal);
        Assert.Contains("```", r.Main.Substring(idx));
    }

    [Fact]
    public void Generate_DriverInstallHistory_UsesMarkdownHeader()
    {
        var drivers = new List<EventLogParser.DriverInstallEvent>
        {
            new(new DateTime(2026, 4, 1), "32.0.15.7250", ""),
        };
        var r = ReportGenerator.Generate(new ReportInput(
            Gpu: TestGpu(),
            System: null,
            Errors: new(),
            DriverInstalls: drivers));

        Assert.Contains("## DRIVER INSTALL HISTORY", r.Main);
        Assert.Contains("32.0.15.7250", r.Main);
    }

    [Fact]
    public void Generate_ErrorTimeline_UsesMarkdownHeader()
    {
        var errors = new List<NvlddmkmError>
        {
            new(new DateTime(2026, 5, 1), 13, "", 0, 1, 2, "Misaligned PC"),
        };
        var r = ReportGenerator.Generate(new ReportInput(
            Gpu: TestGpu(),
            System: null,
            Errors: errors));

        Assert.Contains("## ERROR TIMELINE", r.Main);
    }

    [Fact]
    public void Generate_SystemCrashes_UsesMarkdownHeader()
    {
        var crashes = new List<SystemCrashEvent>
        {
            new(new DateTime(2026, 5, 1), "BSOD", 1001, "VIDEO_TDR_FAILURE"),
        };
        var r = ReportGenerator.Generate(new ReportInput(
            Gpu: TestGpu(),
            System: null,
            Errors: new(),
            Crashes: crashes));

        Assert.Contains("## SYSTEM CRASHES (BSODs, UNEXPECTED REBOOTS)", r.Main);
    }

    [Fact]
    public void Generate_AppCrashCorrelation_UsesMarkdownHeader()
    {
        var t = new DateTime(2026, 5, 1, 12, 0, 0);
        var errors = new List<NvlddmkmError> { new(t, 13, "", 0, 0, 0, "X") };
        var apps = new List<EventLogParser.AppCrashEvent>
        {
            new(t.AddSeconds(5), "game.exe", "nvlddmkm.dll", ""),
        };
        var r = ReportGenerator.Generate(new ReportInput(
            Gpu: TestGpu(),
            System: null,
            Errors: errors,
            AppCrashes: apps));

        Assert.Contains("## APPLICATION CRASH CORRELATION", r.Main);
    }

    [Fact]
    public void Generate_AppCrashCorrelation_GroupedCountsUseTable()
    {
        var t = new DateTime(2026, 5, 1, 12, 0, 0);
        var errors = new List<NvlddmkmError> { new(t, 13, "", 0, 0, 0, "X") };
        var apps = new List<EventLogParser.AppCrashEvent>
        {
            new(t.AddSeconds(5), "game.exe", "nvlddmkm.dll", ""),
        };
        var r = ReportGenerator.Generate(new ReportInput(
            Gpu: TestGpu(),
            System: null,
            Errors: errors,
            AppCrashes: apps));

        Assert.Contains("| Application | Crashes | Correlation pairs |", r.Main);
    }

    [Fact]
    public void Generate_AllAppCrashes_UsesMarkdownHeader()
    {
        var apps = new List<EventLogParser.AppCrashEvent>
        {
            new(new DateTime(2026, 5, 1), "game.exe", "nvlddmkm.dll", ""),
        };
        var r = ReportGenerator.Generate(new ReportInput(
            Gpu: TestGpu(),
            System: null,
            Errors: new(),
            AppCrashes: apps));

        Assert.Contains("## APPLICATION CRASHES (1 total)", r.Main);
    }

    [Fact]
    public void Generate_CrashDumpAnalysis_UsesMarkdownHeader()
    {
        var r = ReportGenerator.Generate(new ReportInput(
            Gpu: TestGpu(),
            System: null,
            Errors: new(),
            DumpAnalysis: "any non-empty body"));

        Assert.Contains("## CRASH DUMP ANALYSIS", r.Main);
    }

    [Fact]
    public void Generate_DumpAnalysisWithCdbBlock_StackTextMovedToDetails()
    {
        var dumpAnalysis =
            "  Mini0001.dmp\n" +
            "    Date:       2026-05-01 14:33:21\n" +
            "    Bugcheck:   0x116 (VIDEO_TDR_FAILURE)\n" +
            "    Parameters: 0x1 0x2 0x3 0x4\n" +
            "    >>> GPU-RELATED CRASH <<<\n" +
            "    --- WinDbg Analysis ---\n" +
            "        BUGCHECK_STR:  0x116\n" +
            "        PROCESS_NAME:  game.exe\n" +
            "        MODULE_NAME: nvlddmkm\n" +
            "        IMAGE_NAME:  nvlddmkm.sys\n" +
            "        FAILURE_BUCKET_ID:  0x116_IMAGE_nvlddmkm.sys\n" +
            "        STACK_TEXT (top frames):\n" +
            "            nt!KeBugCheckEx\n" +
            "            dxgkrnl!TdrCollectDbgInfoStage1\n" +
            "    -----------------------\n";

        var r = ReportGenerator.Generate(new ReportInput(
            Gpu: TestGpu(),
            System: null,
            Errors: new(),
            DumpAnalysis: dumpAnalysis));

        Assert.Contains("### ", r.Main);
        Assert.Contains("Mini0001.dmp", r.Main);
        Assert.Contains("**MODULE_NAME:**", r.Main);
        Assert.Contains("**FAILURE_BUCKET_ID:**", r.Main);
        Assert.DoesNotContain("STACK_TEXT", r.Main);
        Assert.DoesNotContain("nt!KeBugCheckEx", r.Main);
        Assert.DoesNotContain("dxgkrnl!TdrCollectDbgInfoStage1", r.Main);

        Assert.NotNull(r.Details);
        Assert.Contains("STACK_TEXT", r.Details);
        Assert.Contains("nt!KeBugCheckEx", r.Details);

        Assert.Contains($"(./{CdbDetailsSink.DumpsFilenamePlaceholder}#Mini0001.dmp)", r.Main);
        Assert.Contains("### Mini0001.dmp", r.Details);
    }

    [Fact]
    public void Generate_LiveKernelDumpAnalysis_UsesMarkdownHeader()
    {
        var r = ReportGenerator.Generate(new ReportInput(
            Gpu: TestGpu(),
            System: null,
            Errors: new(),
            LiveKernelAnalysis: "any non-null body"));

        Assert.Contains("## LIVE KERNEL DUMP ANALYSIS", r.Main);
    }

    [Fact]
    public void SaveUnique_SubstitutesFilenamePlaceholders()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"flare_save_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var mainWithPlaceholder = $"# Test\n\nSee [`{CdbDetailsSink.DumpsFilenamePlaceholder}`](./{CdbDetailsSink.DumpsFilenamePlaceholder})";
            var detailsWithPlaceholder = $"# Companion to: `{CdbDetailsSink.MainFilenamePlaceholder}`";
            var generated = new GeneratedReport(mainWithPlaceholder, detailsWithPlaceholder);

            var saved = ReportGenerator.SaveUnique(generated, tempDir, new DateTime(2026, 5, 13, 12, 0, 0));

            var mainText = File.ReadAllText(saved.MainPath);
            var detailsText = File.ReadAllText(saved.DetailsPath!);

            Assert.DoesNotContain(CdbDetailsSink.DumpsFilenamePlaceholder, mainText);
            Assert.DoesNotContain(CdbDetailsSink.MainFilenamePlaceholder, detailsText);
            Assert.Contains("flare_report_20260513_120000_dumps.md", mainText);
            Assert.Contains("flare_report_20260513_120000.md", detailsText);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void SaveUnique_NoDetails_StripsTopOfReportPointer()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"flare_save_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var mainWithPointer = $"# Test\n\n> Full crash dump stack traces saved alongside this file as [`{CdbDetailsSink.DumpsFilenamePlaceholder}`](./{CdbDetailsSink.DumpsFilenamePlaceholder}).\n\nBody.";
            var generated = new GeneratedReport(mainWithPointer, Details: null);

            var saved = ReportGenerator.SaveUnique(generated, tempDir, new DateTime(2026, 5, 13, 12, 0, 0));
            var mainText = File.ReadAllText(saved.MainPath);

            Assert.DoesNotContain("Full crash dump stack traces", mainText);
            Assert.Null(saved.DetailsPath);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void Generate_MainReport_ContainsTableOfContents()
    {
        var r = ReportGenerator.Generate(new ReportInput(
            Gpu: TestGpu(),
            System: null,
            Errors: new()));

        Assert.Contains("## Contents", r.Main);
    }

    [Fact]
    public void Generate_TableOfContents_LinksToAllSectionHeaders()
    {
        var errors = new List<NvlddmkmError>
        {
            new(new DateTime(2026, 5, 1, 0, 0, 0), 13, "", 0, 1, 2, "Misaligned PC"),
            new(new DateTime(2026, 5, 8, 0, 0, 0), 13, "", 0, 1, 2, "Misaligned PC"),
        };
        var r = ReportGenerator.Generate(new ReportInput(
            Gpu: TestGpu(),
            System: null,
            Errors: errors));

        var tocStart = r.Main.IndexOf("## Contents", StringComparison.Ordinal);
        Assert.True(tocStart > 0);
        var afterToc = r.Main.Substring(tocStart);

        Assert.Contains("[NVLDDMKM ERROR SUMMARY](#nvlddmkm-error-summary)", afterToc);
        Assert.Contains("[SUMMARY](#summary)", afterToc);
    }

    [Fact]
    public void Generate_TableOfContents_AppearsBeforeFirstSection()
    {
        var r = ReportGenerator.Generate(new ReportInput(
            Gpu: TestGpu(),
            System: null,
            Errors: new()));

        var tocIdx = r.Main.IndexOf("## Contents", StringComparison.Ordinal);
        var firstSectionIdx = r.Main.IndexOf("## NVLDDMKM", StringComparison.Ordinal);
        Assert.True(tocIdx > 0);
        Assert.True(firstSectionIdx > tocIdx, "TOC should come before the first content section");
    }

    [Fact]
    public void Generate_TableOfContents_DoesNotIncludeItself()
    {
        var r = ReportGenerator.Generate(new ReportInput(
            Gpu: TestGpu(),
            System: null,
            Errors: new()));

        var tocStart = r.Main.IndexOf("## Contents", StringComparison.Ordinal);
        var afterToc = r.Main.Substring(tocStart);

        Assert.DoesNotContain("[Contents](#contents)", afterToc);
    }

}
