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
}
