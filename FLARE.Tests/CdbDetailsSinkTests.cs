using FLARE.Core;

namespace FLARE.Tests;

public class CdbDetailsSinkTests
{
    private const string SampleCdbSummary = @"    BUGCHECK_STR:  0x141
    PROCESS_NAME:  System
    MODULE_NAME: nvlddmkm
    IMAGE_NAME:  nvlddmkm.sys
    FAILURE_BUCKET_ID:  LKD_0x141_IMAGE_nvlddmkm.sys
    STACK_TEXT (top frames):
      nt!KeBugCheckEx
      nt!DbgkpWerProcessPolicyResult+0x21
      nt!DbgkWerCaptureLiveKernelDump2+0x1ea
      dxgkrnl!TdrCollectDbgInfoStage1+0xd69
";

    [Fact]
    public void EmitInlineAndArchive_NoStackInInline_FullStackInArchive()
    {
        var sink = new CdbDetailsSink();

        var inline = sink.EmitInlineAndArchive(DumpSection.LiveKernel, "WATCHDOG-X.dmp", SampleCdbSummary);

        Assert.DoesNotContain("STACK_TEXT", inline);
        Assert.DoesNotContain("nt!KeBugCheckEx", inline);
        Assert.DoesNotContain("dxgkrnl!TdrCollectDbgInfoStage1", inline);

        var details = sink.BuildDetailsBody(new DateTime(2026, 5, 13), redactIdentifiers: false, machineName: "");
        Assert.NotNull(details);
        Assert.Contains("STACK_TEXT", details);
        Assert.Contains("nt!KeBugCheckEx", details);
        Assert.Contains("dxgkrnl!TdrCollectDbgInfoStage1", details);
    }

    [Fact]
    public void EmitInlineAndArchive_PreservesStructuredFieldsOnBothSides()
    {
        var sink = new CdbDetailsSink();

        var inline = sink.EmitInlineAndArchive(DumpSection.LiveKernel, "WATCHDOG-X.dmp", SampleCdbSummary);
        var details = sink.BuildDetailsBody(new DateTime(2026, 5, 13), redactIdentifiers: false, machineName: "");

        Assert.NotNull(details);
        foreach (var field in new[] { "BUGCHECK_STR", "PROCESS_NAME", "MODULE_NAME", "IMAGE_NAME", "FAILURE_BUCKET_ID" })
        {
            Assert.Contains(field, inline);
            Assert.Contains(field, details);
        }
    }

    [Fact]
    public void EmitInlineAndArchive_DumpHeaderAppearsAsMarkdownHeading()
    {
        var sink = new CdbDetailsSink();
        sink.EmitInlineAndArchive(DumpSection.LiveKernel, "WATCHDOG-20260512-2148.dmp", SampleCdbSummary);

        var details = sink.BuildDetailsBody(new DateTime(2026, 5, 13), redactIdentifiers: false, machineName: "");
        Assert.NotNull(details);
        Assert.Contains("### WATCHDOG-20260512-2148.dmp", details);
        Assert.DoesNotContain("<details>", details);
        Assert.DoesNotContain("<summary>", details);
        Assert.DoesNotContain("<a id=", details);
    }

    [Fact]
    public void HasContent_FalseUntilFirstEmission()
    {
        var sink = new CdbDetailsSink();
        Assert.False(sink.HasContent);

        sink.EmitInlineAndArchive(DumpSection.LiveKernel, "WATCHDOG-X.dmp", SampleCdbSummary);

        Assert.True(sink.HasContent);
    }

    [Fact]
    public void BuildDetailsBody_NoEmissions_ReturnsNull()
    {
        var sink = new CdbDetailsSink();
        var details = sink.BuildDetailsBody(new DateTime(2026, 5, 13), redactIdentifiers: false, machineName: "");
        Assert.Null(details);
    }

    [Fact]
    public void BuildDetailsBody_HeadersIncludeTimestampAndCompanionPointer()
    {
        var sink = new CdbDetailsSink();
        sink.EmitInlineAndArchive(DumpSection.LiveKernel, "WATCHDOG-X.dmp", SampleCdbSummary);

        var details = sink.BuildDetailsBody(new DateTime(2026, 5, 13, 9, 42, 11), redactIdentifiers: false, machineName: "");

        Assert.NotNull(details);
        Assert.StartsWith("# GPU Error Analysis Report — Dump Details", details);
        Assert.Contains("2026-05-13 09:42:11", details);
        Assert.Contains("Companion to:", details);
    }

    [Fact]
    public void BuildDetailsBody_Redaction_ScrubsCdbContentWhenEnabled()
    {
        var sink = new CdbDetailsSink();
        var cdbWithUserPath = SampleCdbSummary + @"      C:\Users\Alice\game.exe!main+0x42
";
        sink.EmitInlineAndArchive(DumpSection.LiveKernel, "WATCHDOG-X.dmp", cdbWithUserPath);

        var details = sink.BuildDetailsBody(new DateTime(2026, 5, 13), redactIdentifiers: true, machineName: "");

        Assert.NotNull(details);
        Assert.DoesNotContain(@"C:\Users\Alice", details);
        Assert.Contains("%USERPROFILE%", details);
    }

    [Fact]
    public void BuildDetailsBody_Redaction_ScrubsMachineNameWhenEnabled()
    {
        var sink = new CdbDetailsSink();
        var cdbWithMachineName = SampleCdbSummary + "      MyTestHost!worker+0x10\n";
        sink.EmitInlineAndArchive(DumpSection.LiveKernel, "WATCHDOG-X.dmp", cdbWithMachineName);

        var details = sink.BuildDetailsBody(new DateTime(2026, 5, 13), redactIdentifiers: true, machineName: "MyTestHost");

        Assert.NotNull(details);
        Assert.DoesNotContain("MyTestHost", details);
        Assert.Contains("[redacted]", details);
    }

    [Fact]
    public void BuildDetailsBody_RedactDisabled_LeavesMachineNameAlone()
    {
        var sink = new CdbDetailsSink();
        var cdbWithMachineName = SampleCdbSummary + "      MyTestHost!worker+0x10\n";
        sink.EmitInlineAndArchive(DumpSection.LiveKernel, "WATCHDOG-X.dmp", cdbWithMachineName);

        var details = sink.BuildDetailsBody(new DateTime(2026, 5, 13), redactIdentifiers: false, machineName: "MyTestHost");

        Assert.NotNull(details);
        Assert.Contains("MyTestHost", details);
    }

    [Fact]
    public void BuildDetailsBody_CrashDumpsRenderedBeforeLiveKernel()
    {
        var sink = new CdbDetailsSink();
        sink.EmitInlineAndArchive(DumpSection.LiveKernel, "WATCHDOG-A.dmp", SampleCdbSummary);
        sink.EmitInlineAndArchive(DumpSection.CrashDumps, "Mini001.dmp", SampleCdbSummary);

        var details = sink.BuildDetailsBody(new DateTime(2026, 5, 13), redactIdentifiers: false, machineName: "");

        var crashIdx = details!.IndexOf("## CRASH DUMP ANALYSIS", StringComparison.Ordinal);
        var lkIdx = details.IndexOf("## LIVE KERNEL DUMP ANALYSIS", StringComparison.Ordinal);
        Assert.True(crashIdx > 0, "Expected CRASH DUMP ANALYSIS section in details file");
        Assert.True(lkIdx > crashIdx, "CRASH DUMP ANALYSIS should appear before LIVE KERNEL DUMP ANALYSIS in details file");
    }

    [Fact]
    public void BuildDetailsBody_OnlyLiveKernel_OmitsCrashDumpsHeader()
    {
        var sink = new CdbDetailsSink();
        sink.EmitInlineAndArchive(DumpSection.LiveKernel, "WATCHDOG-A.dmp", SampleCdbSummary);

        var details = sink.BuildDetailsBody(new DateTime(2026, 5, 13), redactIdentifiers: false, machineName: "");

        Assert.NotNull(details);
        Assert.DoesNotContain("## CRASH DUMP ANALYSIS", details);
        Assert.Contains("## LIVE KERNEL DUMP ANALYSIS", details);
    }
}
