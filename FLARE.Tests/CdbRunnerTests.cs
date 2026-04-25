using FLARE.Core;

namespace FLARE.Tests;

// ExtractCdbSummary scrapes cdb.exe !analyze -v transcripts for the handful of
// tag lines FLARE embeds in its report. The live CI environment has no WinDbg,
// so the subprocess path is unreachable there — these tests pin the text
// classifier against a captured fixture so a regression in the tag bank doesn't
// silently drop content from the deep-analysis section.
public class CdbRunnerTests
{
    private const string SampleAnalyzeOutput = @"
*******************************************************************************
*                        Bugcheck Analysis                                    *
*******************************************************************************

VIDEO_TDR_FAILURE (116)

BUGCHECK_CODE:  116

BUGCHECK_STR:  0x116_IMAGE_nvlddmkm.sys

PROCESS_NAME:  game.exe

STACK_TEXT:
fffffe85`00abc001 fffff807`12345678 : nt!KeBugCheckEx
fffffe85`00abc002 fffff807`0789abff : watchdog!WdLogEvent5+0x1234
fffffe85`00abc003 fffff807`0789ab12 : nvlddmkm+0x1234

FAULTING_MODULE: fffff8070789a000 nvlddmkm

MODULE_NAME: nvlddmkm

IMAGE_NAME:  nvlddmkm.sys

FAILURE_BUCKET_ID:  0x116_IMAGE_nvlddmkm.sys

OSPLATFORM_TYPE:  x64
";

    [Fact]
    public void ExtractCdbSummary_TaggedLines_AllRetained()
    {
        var result = CdbRunner.ExtractCdbSummary(SampleAnalyzeOutput);

        Assert.NotNull(result);
        Assert.Contains("BUGCHECK_STR:  0x116_IMAGE_nvlddmkm.sys", result);
        Assert.Contains("PROCESS_NAME:  game.exe", result);
        Assert.Contains("MODULE_NAME: nvlddmkm", result);
        Assert.Contains("FAULTING_MODULE: fffff8070789a000 nvlddmkm", result);
        Assert.Contains("IMAGE_NAME:  nvlddmkm.sys", result);
        Assert.Contains("FAILURE_BUCKET_ID:  0x116_IMAGE_nvlddmkm.sys", result);
    }

    [Fact]
    public void ExtractCdbSummary_StackSection_HeaderEmittedAndFramesIndented()
    {
        var result = CdbRunner.ExtractCdbSummary(SampleAnalyzeOutput);

        Assert.NotNull(result);
        Assert.Contains("STACK_TEXT (top frames):", result);
        // Stack frames are emitted with a 6-space lead so they visually nest
        // under the STACK_TEXT header in the final report.
        Assert.Contains("      fffffe85`00abc001 fffff807`12345678 : nt!KeBugCheckEx", result);
        Assert.Contains("      fffffe85`00abc003 fffff807`0789ab12 : nvlddmkm+0x1234", result);
    }

    [Fact]
    public void ExtractCdbSummary_StackSection_TerminatesOnBlankLine()
    {
        // The blank line after the last stack frame closes the stack window —
        // tags below it (FAULTING_MODULE etc.) must not be captured as stack frames.
        var result = CdbRunner.ExtractCdbSummary(SampleAnalyzeOutput);

        Assert.NotNull(result);
        // FAULTING_MODULE line appears via the tag bank (4-space indent),
        // not via the stack branch (6-space indent).
        Assert.Contains("    FAULTING_MODULE: fffff8070789a000 nvlddmkm", result);
        Assert.DoesNotContain("      FAULTING_MODULE:", result);
    }

    [Fact]
    public void ExtractCdbSummary_StackSection_CapsAtTenFramesWithTruncationMarker()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("BUGCHECK_STR:  0xEF");
        sb.AppendLine("STACK_TEXT:");
        for (int i = 0; i < 20; i++)
            sb.AppendLine($"frame_{i:D2}");
        sb.AppendLine();
        sb.AppendLine("MODULE_NAME: foo");

        var result = CdbRunner.ExtractCdbSummary(sb.ToString());

        Assert.NotNull(result);
        Assert.Contains("frame_00", result);
        Assert.Contains("frame_09", result);
        Assert.DoesNotContain("frame_10", result);
        Assert.DoesNotContain("frame_19", result);
        Assert.Contains("(top 10 frames shown; cdb stack was longer)", result);
        Assert.Contains("MODULE_NAME: foo", result);
    }

    [Fact]
    public void ExtractCdbSummary_StackSection_BelowCap_NoTruncationMarker()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("BUGCHECK_STR:  0xEF");
        sb.AppendLine("STACK_TEXT:");
        for (int i = 0; i < 5; i++)
            sb.AppendLine($"frame_{i:D2}");
        sb.AppendLine();
        sb.AppendLine("MODULE_NAME: foo");

        var result = CdbRunner.ExtractCdbSummary(sb.ToString());

        Assert.NotNull(result);
        Assert.Contains("frame_04", result);
        Assert.DoesNotContain("cdb stack was longer", result);
    }

    [Fact]
    public void ExtractCdbSummary_NoTaggedContent_ReturnsNull()
    {
        // Real !analyze -v transcripts always have at least BUGCHECK_STR. If
        // none of the tags appear, treat the output as unusable rather than
        // producing an empty section.
        var result = CdbRunner.ExtractCdbSummary("just some noise\nwith no tags\n");
        Assert.Null(result);
    }

    [Fact]
    public void ExtractCdbSummary_EmptyInput_ReturnsNull()
    {
        Assert.Null(CdbRunner.ExtractCdbSummary(""));
    }

    [Fact]
    public void ExtractCdbSummary_BannerPresentButNoTags_FiresDriftWarning()
    {
        // Canary for a WinDbg format shift: if the Bugcheck Analysis banner
        // appears but none of the tag lines did, the extractor's vocabulary
        // has drifted — log a warning so the silent-empty section gets noticed.
        var input =
            "*******************************************************************************\n" +
            "*                        Bugcheck Analysis                                    *\n" +
            "*******************************************************************************\n" +
            "\n" +
            "some future cdb format with entirely different labels\n";
        var logs = new List<string>();

        var result = CdbRunner.ExtractCdbSummary(input, logs.Add);

        Assert.Null(result);
        Assert.Single(logs);
        Assert.Contains("cdb summary extractor may need updating", logs[0]);
    }

    [Fact]
    public void ExtractCdbSummary_NoBanner_DoesNotFireWarning()
    {
        // Without the banner we have no evidence a real !analyze -v ran; the
        // transcript could just be a startup error. Don't cry wolf.
        var logs = new List<string>();

        var result = CdbRunner.ExtractCdbSummary("some noise that is not cdb output at all", logs.Add);

        Assert.Null(result);
        Assert.Empty(logs);
    }

    [Fact]
    public void ExtractCdbSummary_BannerAndTagsBothPresent_NoWarning()
    {
        var logs = new List<string>();

        var result = CdbRunner.ExtractCdbSummary(SampleAnalyzeOutput, logs.Add);

        Assert.NotNull(result);
        Assert.Empty(logs);
    }

    [Fact]
    public void ExtractCdbSummary_BannerWithoutTagsAndHealth_RecordsCanary()
    {
        var input =
            "*******************************************************************************\n" +
            "*                        Bugcheck Analysis                                    *\n" +
            "*******************************************************************************\n" +
            "\n" +
            "some future cdb format with entirely different labels\n";
        var health = new CollectorHealth();

        CdbRunner.ExtractCdbSummary(input, log: null, health);

        Assert.Single(health.Notices);
        Assert.Equal(CollectorNoticeKind.Canary, health.Notices[0].Kind);
        Assert.Equal("cdb summary extractor", health.Notices[0].Source);
    }

    [Fact]
    public void ExtractCdbSummary_BannerAndTagsPresentWithHealth_DoesNotRecordCanary()
    {
        var health = new CollectorHealth();

        CdbRunner.ExtractCdbSummary(SampleAnalyzeOutput, log: null, health);

        Assert.Empty(health.Notices);
    }
}
