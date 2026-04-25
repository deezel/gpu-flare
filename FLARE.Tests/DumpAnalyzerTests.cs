using System.Text;
using FLARE.Core;

namespace FLARE.Tests;

public class DumpAnalyzerTests : IDisposable
{
    private readonly string _tempDir;

    public DumpAnalyzerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"flare_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private string WriteDump(string name, byte[] data)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, data);
        return path;
    }

    private static byte[] BuildPageDu64(uint bugcheck, ulong p1 = 0, ulong p2 = 0, ulong p3 = 0, ulong p4 = 0)
    {
        var data = new byte[256];
        Encoding.ASCII.GetBytes("PAGEDU64").CopyTo(data, 0);
        BitConverter.GetBytes(bugcheck).CopyTo(data, 0x38);
        BitConverter.GetBytes((uint)0).CopyTo(data, 0x3C);
        BitConverter.GetBytes(p1).CopyTo(data, 0x40);
        BitConverter.GetBytes(p2).CopyTo(data, 0x48);
        BitConverter.GetBytes(p3).CopyTo(data, 0x50);
        BitConverter.GetBytes(p4).CopyTo(data, 0x58);
        return data;
    }

    // Build a synthetic MDMP ("user-mode" minidump shape, reused for kernel-style
    // bugcheck values). Layout mirrors what ParseMdmp reads:
    //   Header: MDMP sig, version, streamCount=1, streamDirRva=0x20, checksum, timeDateStamp
    //   Stream dir at 0x20: sType=6 (MINIDUMP_EXCEPTION_STREAM), sSize, sRva=0x40
    //   Exception stream at 0x40: ThreadId, alignment, ExceptionCode, ExceptionFlags,
    //     ExceptionRecord (u64), ExceptionAddress (u64), NumberParameters,
    //     alignment, ExceptionInformation[0..3] (u64 each)
    private static byte[] BuildMdmp(uint exceptionCode, uint numParams = 4,
        ulong p1 = 0, ulong p2 = 0, ulong p3 = 0, ulong p4 = 0, uint timeDateStamp = 0)
    {
        var data = new byte[256];
        BitConverter.GetBytes((uint)0x504D444D).CopyTo(data, 0x00); // 'MDMP'
        BitConverter.GetBytes((ushort)0xA793).CopyTo(data, 0x04);   // version
        BitConverter.GetBytes((ushort)0).CopyTo(data, 0x06);        // implVersion
        BitConverter.GetBytes((uint)1).CopyTo(data, 0x08);          // streamCount
        BitConverter.GetBytes((uint)0x20).CopyTo(data, 0x0C);       // streamDirRva
        BitConverter.GetBytes((uint)0).CopyTo(data, 0x10);          // checksum
        BitConverter.GetBytes(timeDateStamp).CopyTo(data, 0x14);    // timeDateStamp

        // Stream directory entry
        BitConverter.GetBytes((uint)6).CopyTo(data, 0x20);          // sType = ExceptionStream
        BitConverter.GetBytes((uint)168).CopyTo(data, 0x24);        // sSize
        BitConverter.GetBytes((uint)0x40).CopyTo(data, 0x28);       // sRva

        // Exception stream
        BitConverter.GetBytes((uint)0).CopyTo(data, 0x40);          // ThreadId
        BitConverter.GetBytes((uint)0).CopyTo(data, 0x44);          // alignment
        BitConverter.GetBytes(exceptionCode).CopyTo(data, 0x48);    // ExceptionCode
        BitConverter.GetBytes((uint)0).CopyTo(data, 0x4C);          // ExceptionFlags
        BitConverter.GetBytes((ulong)0).CopyTo(data, 0x50);         // ExceptionRecord
        BitConverter.GetBytes((ulong)0).CopyTo(data, 0x58);         // ExceptionAddress
        BitConverter.GetBytes(numParams).CopyTo(data, 0x60);        // NumberParameters
        BitConverter.GetBytes((uint)0).CopyTo(data, 0x64);          // alignment
        BitConverter.GetBytes(p1).CopyTo(data, 0x68);
        BitConverter.GetBytes(p2).CopyTo(data, 0x70);
        BitConverter.GetBytes(p3).CopyTo(data, 0x78);
        BitConverter.GetBytes(p4).CopyTo(data, 0x80);
        return data;
    }

    // MDMP with no ExceptionStream — simulates an "unsupported shape" where the
    // parser has nothing to extract. Pinned so behavior (null-or-zero bugcheck)
    // stays predictable if a user-mode dump with only ThreadList/SystemInfo
    // streams lands in the minidump dir.
    private static byte[] BuildMdmpNoExceptionStream()
    {
        var data = new byte[128];
        BitConverter.GetBytes((uint)0x504D444D).CopyTo(data, 0x00);
        BitConverter.GetBytes((ushort)0xA793).CopyTo(data, 0x04);
        BitConverter.GetBytes((ushort)0).CopyTo(data, 0x06);
        BitConverter.GetBytes((uint)1).CopyTo(data, 0x08);
        BitConverter.GetBytes((uint)0x20).CopyTo(data, 0x0C);
        BitConverter.GetBytes((uint)0).CopyTo(data, 0x10);
        BitConverter.GetBytes((uint)0).CopyTo(data, 0x14);

        // Stream type 3 (ThreadListStream), not ExceptionStream
        BitConverter.GetBytes((uint)3).CopyTo(data, 0x20);
        BitConverter.GetBytes((uint)4).CopyTo(data, 0x24);
        BitConverter.GetBytes((uint)0x40).CopyTo(data, 0x28);
        return data;
    }

    [Fact]
    public void AnalyzeDump_VideoTdrFailure_DetectedAsGpuRelated()
    {
        var path = WriteDump("tdr.dmp", BuildPageDu64(0x116));
        var result = DumpAnalyzer.AnalyzeDump(path);
        Assert.NotNull(result);
        Assert.Equal("VIDEO_TDR_FAILURE", result.BugcheckName);
        Assert.True(result.IsGpuRelated);
        Assert.Equal((uint)0x116, result.BugcheckCode);
    }

    [Fact]
    public void AnalyzeDump_VideoSchedulerError_DetectedAsGpuRelated()
    {
        var path = WriteDump("sched.dmp", BuildPageDu64(0x119));
        var result = DumpAnalyzer.AnalyzeDump(path);
        Assert.NotNull(result);
        Assert.Equal("VIDEO_SCHEDULER_INTERNAL_ERROR", result.BugcheckName);
        Assert.True(result.IsGpuRelated);
    }

    [Fact]
    public void AnalyzeDump_NonGpuBugcheck_NotGpuRelated()
    {
        var path = WriteDump("critproc.dmp", BuildPageDu64(0xEF));
        var result = DumpAnalyzer.AnalyzeDump(path);
        Assert.NotNull(result);
        Assert.Equal("CRITICAL_PROCESS_DIED", result.BugcheckName);
        Assert.False(result.IsGpuRelated);
    }

    [Fact]
    public void AnalyzeDump_ParametersExtracted()
    {
        var path = WriteDump("params.dmp", BuildPageDu64(0x116, p1: 0xDEAD, p2: 0xBEEF, p3: 0xCAFE, p4: 0xF00D));
        var result = DumpAnalyzer.AnalyzeDump(path);
        Assert.NotNull(result);
        Assert.Equal((ulong)0xDEAD, result.Param1);
        Assert.Equal((ulong)0xBEEF, result.Param2);
        Assert.Equal((ulong)0xCAFE, result.Param3);
        Assert.Equal((ulong)0xF00D, result.Param4);
    }

    [Fact]
    public void AnalyzeDump_TooSmallFile_ReturnsNull()
    {
        var path = WriteDump("tiny.dmp", new byte[32]);
        Assert.Null(DumpAnalyzer.AnalyzeDump(path));
    }

    [Fact]
    public void AnalyzeDump_UnknownSignature_ReturnsNull()
    {
        var data = new byte[256];
        Encoding.ASCII.GetBytes("GARBAGE!").CopyTo(data, 0);
        var path = WriteDump("unknown.dmp", data);
        Assert.Null(DumpAnalyzer.AnalyzeDump(path));
    }

    [Fact]
    public void AnalyzeDump_32BitDump_ParsesCorrectly()
    {
        var data = new byte[256];
        Encoding.ASCII.GetBytes("PAGE").CopyTo(data, 0);
        data[4] = 0x00; data[5] = 0x00; data[6] = 0x00; data[7] = 0x00;
        BitConverter.GetBytes((uint)0x116).CopyTo(data, 0x20);
        BitConverter.GetBytes((uint)0xAA).CopyTo(data, 0x24);
        BitConverter.GetBytes((uint)0xBB).CopyTo(data, 0x28);
        BitConverter.GetBytes((uint)0xCC).CopyTo(data, 0x2C);
        BitConverter.GetBytes((uint)0xDD).CopyTo(data, 0x30);
        var path = WriteDump("dump32.dmp", data);
        var result = DumpAnalyzer.AnalyzeDump(path);
        Assert.NotNull(result);
        Assert.Equal("VIDEO_TDR_FAILURE", result.BugcheckName);
        Assert.True(result.IsGpuRelated);
        Assert.Equal((ulong)0xAA, result.Param1);
    }

    [Fact]
    public void AnalyzeDump_FallbackScan_IgnoresSmallCodesOutsideHeaderBand()
    {
        // Build a malformed PAGEDU64 header where:
        //   - 0x38 holds 0xDEADBEEF (triggers the fallback, not in bugcheck table, > 0x10000)
        //   - 0x40 holds 0 (primary retry also fails)
        //   - 0x100 holds 0x0A (IRQL_NOT_LESS_OR_EQUAL) — far outside the 0x30..0x80 band
        // The tightened fallback must NOT pick up the 0x0A at 0x100 and misreport
        // this as IRQL_NOT_LESS_OR_EQUAL. The whole point of the narrow band is that
        // pointer/length fragments deep in the header can legitimately equal small
        // bugcheck codes.
        var data = new byte[512];
        Encoding.ASCII.GetBytes("PAGEDU64").CopyTo(data, 0);
        BitConverter.GetBytes((uint)0xDEADBEEF).CopyTo(data, 0x38);
        BitConverter.GetBytes((uint)0x0).CopyTo(data, 0x40);
        BitConverter.GetBytes((uint)0x0A).CopyTo(data, 0x100);

        var path = WriteDump("malformed.dmp", data);
        var result = DumpAnalyzer.AnalyzeDump(path);

        Assert.NotNull(result);
        Assert.NotEqual((uint)0x0A, result.BugcheckCode);
        Assert.False(result.IsGpuRelated);
    }

    [Fact]
    public void AnalyzeDump_FallbackScan_FindsBugcheckWithinBand()
    {
        // Primary 0x38 and 0x40 fail; a valid bugcheck (0x116 VIDEO_TDR_FAILURE) sits
        // at offset 0x48 — still within the 0x30..0x80 scan band. The fallback must
        // still find it.
        var data = new byte[256];
        Encoding.ASCII.GetBytes("PAGEDU64").CopyTo(data, 0);
        BitConverter.GetBytes((uint)0xDEADBEEF).CopyTo(data, 0x38);
        BitConverter.GetBytes((uint)0).CopyTo(data, 0x40);
        BitConverter.GetBytes((uint)0x116).CopyTo(data, 0x48);
        BitConverter.GetBytes((uint)0).CopyTo(data, 0x4C); // padding

        var path = WriteDump("fallback_ok.dmp", data);
        var result = DumpAnalyzer.AnalyzeDump(path);

        Assert.NotNull(result);
        Assert.Equal((uint)0x116, result.BugcheckCode);
        Assert.True(result.IsGpuRelated);
    }

    [Fact]
    public void AnalyzeDump_MdmpKernelStyleBugcheck_DetectedAsGpuRelated()
    {
        var path = WriteDump("mdmp_kernel.dmp",
            BuildMdmp(0x116, numParams: 4, p1: 0x1, p2: 0x2, p3: 0x3, p4: 0x4));
        var result = DumpAnalyzer.AnalyzeDump(path);
        Assert.NotNull(result);
        Assert.Equal((uint)0x116, result.BugcheckCode);
        Assert.Equal("VIDEO_TDR_FAILURE", result.BugcheckName);
        Assert.True(result.IsGpuRelated);
        Assert.Equal((ulong)0x1, result.Param1);
        Assert.Equal((ulong)0x2, result.Param2);
        Assert.Equal((ulong)0x3, result.Param3);
        Assert.Equal((ulong)0x4, result.Param4);
    }

    [Fact]
    public void AnalyzeDump_MdmpUserModeAccessViolation_NotFlaggedAsGpu()
    {
        // 0xC0000005 (EXCEPTION_ACCESS_VIOLATION) is the common user-mode exception
        // code. It must not be classified as a GPU bugcheck, and the formatted name
        // should fall back to hex since the catalog doesn't know it.
        var path = WriteDump("mdmp_user.dmp", BuildMdmp(0xC0000005, numParams: 2, p1: 0xDEAD, p2: 0xBEEF));
        var result = DumpAnalyzer.AnalyzeDump(path);
        Assert.NotNull(result);
        Assert.Equal((uint)0xC0000005, result.BugcheckCode);
        Assert.Equal("0xC0000005", result.BugcheckName);
        Assert.False(result.IsGpuRelated);
        // The value read from MDMP stream 6 is EXCEPTION_RECORD.ExceptionCode
        // (NTSTATUS), not a kernel BugCheckCode — flag it so the renderer can
        // label it as an Exception rather than a Bugcheck.
        Assert.True(result.IsUserModeException);
        Assert.Equal((ulong)0xDEAD, result.Param1);
        Assert.Equal((ulong)0xBEEF, result.Param2);
        Assert.Equal((ulong)0, result.Param3);
        Assert.Equal((ulong)0, result.Param4);
    }

    [Fact]
    public void AnalyzeDump_MdmpKernelStyleBugcheck_NotFlaggedAsUserMode()
    {
        // 0x116 is in BugcheckCatalog — not a user-mode exception code. Pinned
        // here to keep kernel-style MDMPs from accidentally routing through the
        // user-mode rendering path alongside IsGpuRelated=true.
        var path = WriteDump("mdmp_kernel2.dmp", BuildMdmp(0x116));
        var result = DumpAnalyzer.AnalyzeDump(path);
        Assert.NotNull(result);
        Assert.False(result.IsUserModeException);
        Assert.True(result.IsGpuRelated);
    }

    [Fact]
    public void AnalyzeDump_MdmpWithoutExceptionStream_NotFlaggedAsUserMode()
    {
        // An MDMP missing stream 6 has bugcheck=0. That's neither a kernel
        // crash nor a real exception code — rendering "Exception: 0x00000000"
        // would be just as misleading as rendering "Bugcheck: 0x00000000".
        // IsUserModeException must stay false so the pre-existing "unknown
        // shape" rendering keeps applying.
        var path = WriteDump("mdmp_no_exc2.dmp", BuildMdmpNoExceptionStream());
        var result = DumpAnalyzer.AnalyzeDump(path);
        Assert.NotNull(result);
        Assert.False(result.IsUserModeException);
    }

    [Fact]
    public void AnalyzeDump_MdmpWithoutExceptionStream_ReturnsZeroBugcheck()
    {
        // An MDMP that contains only non-exception streams (e.g. ThreadListStream)
        // has no bugcheck/exception code to extract. Pinned so future changes can't
        // accidentally emit a misleading GPU classification for a user-mode dump
        // that happens to lack stream type 6.
        var path = WriteDump("mdmp_no_exc.dmp", BuildMdmpNoExceptionStream());
        var result = DumpAnalyzer.AnalyzeDump(path);
        Assert.NotNull(result);
        Assert.Equal((uint)0, result.BugcheckCode);
        Assert.False(result.IsGpuRelated);
    }

    [Fact]
    public void GenerateDumpReport_NoDumps_ReportsNone()
    {
        var result = DumpAnalyzer.GenerateDumpReport(_tempDir);
        Assert.Contains("No minidump files found", result);
    }

    [Fact]
    public void GenerateDumpReport_WithDumps_CountsGpuCrashes()
    {
        WriteDump("gpu.dmp", BuildPageDu64(0x116));
        WriteDump("other.dmp", BuildPageDu64(0xEF));
        var result = DumpAnalyzer.GenerateDumpReport(_tempDir);
        Assert.Contains("GPU-related crashes: 1 of 2", result);
    }

    [Fact]
    public void GenerateDumpReport_CutoffExcludesOlderDumps()
    {
        // The Minidumps directory accumulates dumps from every prior run. A
        // report claiming "last 7 days" must not analyze a 6-month-old dump.
        var recent = WriteDump("recent.dmp", BuildPageDu64(0x116));
        var old = WriteDump("old.dmp", BuildPageDu64(0x116));
        File.SetLastWriteTime(old, DateTime.Now.AddDays(-180));
        File.SetLastWriteTime(recent, DateTime.Now.AddHours(-1));

        var cutoff = DateTime.Now.AddDays(-7);
        var result = DumpAnalyzer.GenerateDumpReport(_tempDir, cutoff: cutoff);

        Assert.Contains("recent.dmp", result);
        Assert.DoesNotContain("old.dmp", result);
        Assert.Contains("Analyzed 1 crash dump(s)", result);
    }

    [Fact]
    public void GenerateDumpReport_CutoffExcludesAllDumps_ExplainsWhy()
    {
        // When every on-disk dump is pre-cutoff, the report must not say
        // "No minidump files found" (misleading — they exist, just not in
        // the window). Emit a distinct message so the reader understands
        // the Minidumps directory isn't empty.
        var old = WriteDump("old.dmp", BuildPageDu64(0x116));
        File.SetLastWriteTime(old, DateTime.Now.AddDays(-180));

        var cutoff = DateTime.Now.AddDays(-7);
        var result = DumpAnalyzer.GenerateDumpReport(_tempDir, cutoff: cutoff);

        Assert.Contains("older than cutoff", result);
        Assert.DoesNotContain("No minidump files found", result);
    }

    [Fact]
    public void GenerateDumpReport_NullCutoff_IncludesEverything()
    {
        var old = WriteDump("old.dmp", BuildPageDu64(0x116));
        File.SetLastWriteTime(old, DateTime.Now.AddDays(-180));

        var result = DumpAnalyzer.GenerateDumpReport(_tempDir);

        Assert.Contains("old.dmp", result);
    }

    [Fact]
    public void AnalyzeDump_DocumentedOffset_NotFlaggedAsHeuristic()
    {
        var path = WriteDump("direct.dmp", BuildPageDu64(0x116));
        var result = DumpAnalyzer.AnalyzeDump(path);
        Assert.NotNull(result);
        Assert.False(result.IsHeuristicMatch);
    }

    [Fact]
    public void AnalyzeDump_FallbackScan_MarksResultAsHeuristic()
    {
        // Same layout as AnalyzeDump_FallbackScan_FindsBugcheckWithinBand —
        // 0x38 holds garbage, 0x40 holds zero, a real catalog value sits at
        // 0x48 (inside the 0x30..0x80 band). Having extracted it via the scan
        // path means the renderer should annotate the result as a best guess.
        var data = new byte[256];
        Encoding.ASCII.GetBytes("PAGEDU64").CopyTo(data, 0);
        BitConverter.GetBytes((uint)0xDEADBEEF).CopyTo(data, 0x38);
        BitConverter.GetBytes((uint)0).CopyTo(data, 0x40);
        BitConverter.GetBytes((uint)0x116).CopyTo(data, 0x48);
        BitConverter.GetBytes((uint)0).CopyTo(data, 0x4C);
        var path = WriteDump("heuristic.dmp", data);

        var result = DumpAnalyzer.AnalyzeDump(path);

        Assert.NotNull(result);
        Assert.Equal((uint)0x116, result.BugcheckCode);
        Assert.True(result.IsHeuristicMatch);
    }

    [Fact]
    public void GenerateDumpReport_HeuristicMatch_AnnotatesLine()
    {
        var data = new byte[256];
        Encoding.ASCII.GetBytes("PAGEDU64").CopyTo(data, 0);
        BitConverter.GetBytes((uint)0xDEADBEEF).CopyTo(data, 0x38);
        BitConverter.GetBytes((uint)0).CopyTo(data, 0x40);
        BitConverter.GetBytes((uint)0x116).CopyTo(data, 0x48);
        BitConverter.GetBytes((uint)0).CopyTo(data, 0x4C);
        WriteDump("heur_render.dmp", data);

        var result = DumpAnalyzer.GenerateDumpReport(_tempDir);

        Assert.Contains("heuristic match", result);
    }

    [Fact]
    public void GenerateDumpReport_GpuRelatedTotal_AnnotatesHeuristicContribution()
    {
        var data = new byte[256];
        Encoding.ASCII.GetBytes("PAGEDU64").CopyTo(data, 0);
        BitConverter.GetBytes((uint)0xDEADBEEF).CopyTo(data, 0x38);
        BitConverter.GetBytes((uint)0).CopyTo(data, 0x40);
        BitConverter.GetBytes((uint)0x116).CopyTo(data, 0x48);
        BitConverter.GetBytes((uint)0).CopyTo(data, 0x4C);
        WriteDump("heur_gpu.dmp", data);
        WriteDump("direct_gpu.dmp", BuildPageDu64(0x116));
        WriteDump("other.dmp", BuildPageDu64(0xEF));

        var result = DumpAnalyzer.GenerateDumpReport(_tempDir);

        Assert.Contains("GPU-related crashes: 2 of 3 (1 via heuristic match)", result);
    }

    [Fact]
    public void GenerateDumpReport_UserModeDump_RendersExceptionLabelNotBugcheck()
    {
        // User-mode MDMP must never be labelled "Bugcheck" — that's the whole
        // point of the IsUserModeException flag: a reader scanning the report
        // shouldn't confuse an application access violation with a kernel BSOD.
        WriteDump("user_render.dmp", BuildMdmp(0xC0000005));

        var result = DumpAnalyzer.GenerateDumpReport(_tempDir);

        Assert.Contains("Exception:", result);
        Assert.Contains("user-mode dump", result);
        Assert.DoesNotMatch(new System.Text.RegularExpressions.Regex(@"Bugcheck:\s+0xC0000005"), result);
    }

    [Fact]
    public void GenerateDumpReport_KernelBugcheck_StillRendersBugcheckLabel()
    {
        // A documented kernel bugcheck stays on the Bugcheck rendering path —
        // the Exception path must not leak across into normal PAGEDU64 flow.
        WriteDump("kernel_render.dmp", BuildPageDu64(0x116));

        var result = DumpAnalyzer.GenerateDumpReport(_tempDir);

        Assert.Contains("Bugcheck:", result);
        Assert.Contains("VIDEO_TDR_FAILURE", result);
        Assert.DoesNotContain("user-mode dump", result);
    }
}
