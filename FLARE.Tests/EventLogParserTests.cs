using FLARE.Core;

namespace FLARE.Tests;

public class SetupApiLogTests : IDisposable
{
    private readonly string _tempFile;

    public SetupApiLogTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"flare_setupapi_test_{Guid.NewGuid():N}.log");
    }

    public void Dispose()
    {
        try { File.Delete(_tempFile); } catch { }
    }

    [Fact]
    public void ParseSetupApiLog_CurrentGenDriver_Parsed()
    {
        File.WriteAllLines(_tempFile, [
            @"Boot Session: 2025/03/15 10:00:00",
            @"Install Device - PCI\VEN_10DE&DEV_2684&SUBSYS_0000&REV_00\{4D36E968-E325-11CE-BFC1-08002BE10318} 10:05:00.000",
            @"inf:   Driver Version = 6.14,32.0.15.8129"
        ]);
        var result = EventLogParser.ParseSetupApiLog(_tempFile);
        Assert.Single(result);
        Assert.Equal("32.0.15.8129", result[0].DriverVersion);
    }

    [Fact]
    public void ParseSetupApiLog_OlderGenDriver_Parsed()
    {
        File.WriteAllLines(_tempFile, [
            @"Boot Session: 2024/06/01 10:00:00",
            @"Install Device - PCI\VEN_10DE&DEV_2684&SUBSYS_0000&REV_00\{4D36E968-E325-11CE-BFC1-08002BE10318} 10:05:00.000",
            @"inf:   Driver Version = 6.14,31.0.15.5599"
        ]);
        var result = EventLogParser.ParseSetupApiLog(_tempFile);
        Assert.Single(result);
        Assert.Equal("31.0.15.5599", result[0].DriverVersion);
    }

    [Fact]
    public void ParseSetupApiLog_FutureGenDriver_Parsed()
    {
        File.WriteAllLines(_tempFile, [
            @"Boot Session: 2026/01/01 10:00:00",
            @"Install Device - PCI\VEN_10DE&DEV_2684&SUBSYS_0000&REV_00\{4D36E968-E325-11CE-BFC1-08002BE10318} 10:05:00.000",
            @"inf:   Driver Version = 6.14,33.0.15.1234"
        ]);
        var result = EventLogParser.ParseSetupApiLog(_tempFile);
        Assert.Single(result);
        Assert.Equal("33.0.15.1234", result[0].DriverVersion);
    }

    [Fact]
    public void ParseSetupApiLog_EmptyFile_ReturnsEmpty()
    {
        File.WriteAllText(_tempFile, "");
        var result = EventLogParser.ParseSetupApiLog(_tempFile);
        Assert.Empty(result);
    }
}

public class EventLogParserTests
{
    [Fact]
    public void ParseGpuErrorLine_SmCoordinates_Extracted()
    {
        var line = "2025-01-15 10:30:00~13~Graphics SM Warp Exception on GPC 3, TPC 1, SM 0: Illegal Instruction Encoding";
        var result = EventLogParser.ParseGpuErrorLine(line);
        Assert.NotNull(result);
        Assert.Equal(3, result.Gpc);
        Assert.Equal(1, result.Tpc);
        Assert.Equal(0, result.Sm);
        Assert.Equal("Illegal Instruction Encoding", result.ErrorType);
        Assert.Equal(13, result.EventId);
    }

    [Fact]
    public void ParseGpuErrorLine_TdrEvent153_ClassifiedCorrectly()
    {
        var line = "2025-01-15 10:30:00~153~Display driver nvlddmkm stopped responding";
        var result = EventLogParser.ParseGpuErrorLine(line);
        Assert.NotNull(result);
        Assert.Equal("TDR (Timeout Detection and Recovery)", result.ErrorType);
        Assert.Null(result.Gpc);
    }

    [Fact]
    public void ParseGpuErrorLine_EccError_ClassifiedCorrectly()
    {
        var line = "2025-01-15 10:30:00~14~An uncorrectable ECC error was detected";
        var result = EventLogParser.ParseGpuErrorLine(line);
        Assert.NotNull(result);
        Assert.Equal("Uncorrectable ECC Error", result.ErrorType);
    }

    [Fact]
    public void ParseGpuErrorLine_SramError_ClassifiedCorrectly()
    {
        var line = "2025-01-15 10:30:00~14~Uncorrectable SRAM Error detected";
        var result = EventLogParser.ParseGpuErrorLine(line);
        Assert.NotNull(result);
        Assert.Equal("Uncorrectable SRAM Error", result.ErrorType);
    }

    [Fact]
    public void ParseGpuErrorLine_CmdreError_ClassifiedCorrectly()
    {
        var line = "2025-01-15 10:30:00~14~CMDre 0A 1B 2C";
        var result = EventLogParser.ParseGpuErrorLine(line);
        Assert.NotNull(result);
        Assert.Equal("Command Re-execution Error (CMDre)", result.ErrorType);
    }

    [Fact]
    public void ParseGpuErrorLine_EsrError_ClassifiedCorrectly()
    {
        var line = "2025-01-15 10:30:00~13~Graphics Exception: ESR 0x00000040";
        var result = EventLogParser.ParseGpuErrorLine(line);
        Assert.NotNull(result);
        Assert.Equal("Graphics Exception (ESR)", result.ErrorType);
    }

    [Fact]
    public void ParseGpuErrorLine_MalformedLine_ReturnsNull()
    {
        Assert.Null(EventLogParser.ParseGpuErrorLine("garbage"));
        Assert.Null(EventLogParser.ParseGpuErrorLine(""));
    }

    [Fact]
    public void ParseGpuErrorLine_NoSmCoords_CoordsAreNull()
    {
        var line = "2025-01-15 10:30:00~14~PCIE error Uncorrectable something";
        var result = EventLogParser.ParseGpuErrorLine(line);
        Assert.NotNull(result);
        Assert.Null(result.Gpc);
        Assert.Null(result.Tpc);
        Assert.Null(result.Sm);
    }

    [Theory]
    [InlineData("Illegal Instruction Encoding")]
    [InlineData("Multiple Warp Errors")]
    [InlineData("Illegal Global Access")]
    [InlineData("Page Fault")]
    [InlineData("Misaligned Address")]
    [InlineData("Misaligned PC")]
    public void ParseGpuErrorLine_AllExceptionTypes_Recognized(string errorType)
    {
        var line = $"2025-01-15 10:30:00~13~Graphics SM Warp Exception on GPC 0, TPC 0, SM 0: {errorType}";
        var result = EventLogParser.ParseGpuErrorLine(line);
        Assert.NotNull(result);
        Assert.Equal(errorType, result.ErrorType);
    }
}
