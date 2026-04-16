using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FLARE.Core;

public static class DumpAnalyzer
{
    static readonly Dictionary<uint, string> BugcheckNames = new()
    {
        { 0x0A,  "IRQL_NOT_LESS_OR_EQUAL" },
        { 0x1A,  "MEMORY_MANAGEMENT" },
        { 0x3B,  "SYSTEM_SERVICE_EXCEPTION" },
        { 0x50,  "PAGE_FAULT_IN_NONPAGED_AREA" },
        { 0x7E,  "SYSTEM_THREAD_EXCEPTION_NOT_HANDLED" },
        { 0x7F,  "UNEXPECTED_KERNEL_MODE_TRAP" },
        { 0xD1,  "DRIVER_IRQL_NOT_LESS_OR_EQUAL" },
        { 0xEF,  "CRITICAL_PROCESS_DIED" },
        { 0x10D, "WDF_VIOLATION" },
        { 0x116, "VIDEO_TDR_FAILURE" },
        { 0x117, "VIDEO_TDR_TIMEOUT_DETECTED" },
        { 0x119, "VIDEO_SCHEDULER_INTERNAL_ERROR" },
        { 0x133, "DPC_WATCHDOG_VIOLATION" },
        { 0x278, "KERNEL_MODE_HEAP_CORRUPTION" },
        { 0x307, "KERNEL_STORAGE_SLOT_IN_USE" },
    };

    public record DumpInfo(
        string FileName,
        DateTime Timestamp,
        uint BugcheckCode,
        string BugcheckName,
        ulong Param1, ulong Param2, ulong Param3, ulong Param4,
        bool IsGpuRelated
    );

    public static DumpInfo? AnalyzeDump(string path, Action<string>? log = null)
    {
        try
        {
            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs);

            if (fs.Length < 64) return null;

            var sig = br.ReadBytes(8);
            var sigStr = System.Text.Encoding.ASCII.GetString(sig, 0, 8).TrimEnd('\0');

            if (sigStr.StartsWith("PAGEDU64") || sigStr.StartsWith("PAGEDUMP"))
                return ParseKernelDump64(path, fs, br);

            fs.Seek(0, SeekOrigin.Begin);
            uint mdmpSig = br.ReadUInt32();
            if (mdmpSig == 0x504D444D)
                return ParseMdmp(path, fs, br);

            fs.Seek(0, SeekOrigin.Begin);
            var sig4 = System.Text.Encoding.ASCII.GetString(br.ReadBytes(4));
            if (sig4 == "PAGE")
                return ParseKernelDump32(path, fs, br);

            log?.Invoke($"  {Path.GetFileName(path)}: Unknown format (sig: {sigStr})");
            return null;
        }
        catch (Exception ex)
        {
            log?.Invoke($"  {Path.GetFileName(path)}: {ex.Message}");
            return null;
        }
    }

    // PAGEDU64 kernel dump header layout (Windows 10/11):
    // Reference: Windows Internals 7th ed., Chapter 15; ReactOS kdtypes.h
    //
    // Offset  Size  Field
    // 0x00    8     Signature ("PAGEDU64" or "PAGEDUMP")
    // 0x08    4     ValidDump signature
    // 0x0C    4     MajorVersion
    // 0x10    4     MinorVersion
    // 0x14    4     DirectoryTableBase (low)
    // 0x18    4     DirectoryTableBase (high)
    // 0x1C    4     PfnDatabase (low)
    // ...
    // 0x38    4     BugCheckCode
    // 0x3C    4     Padding (alignment to 8-byte boundary)
    // 0x40    8     BugCheckParameter1
    // 0x48    8     BugCheckParameter2
    // 0x50    8     BugCheckParameter3
    // 0x58    8     BugCheckParameter4
    //
    // Some dump writers place the bugcheck at 0x40 instead of 0x38 (observed in
    // older Windows versions). The fallback reads at 0x40, then scans the first
    // 512 bytes as a last resort.
    static DumpInfo? ParseKernelDump64(string path, FileStream fs, BinaryReader br)
    {
        uint bugcheck = 0;
        ulong p1 = 0, p2 = 0, p3 = 0, p4 = 0;

        fs.Seek(0x38, SeekOrigin.Begin);
        bugcheck = br.ReadUInt32();
        uint padding = br.ReadUInt32();
        p1 = br.ReadUInt64();
        p2 = br.ReadUInt64();
        p3 = br.ReadUInt64();
        p4 = br.ReadUInt64();

        if (!BugcheckNames.ContainsKey(bugcheck) && bugcheck > 0x10000)
        {
            fs.Seek(0x40, SeekOrigin.Begin);
            var bc2 = br.ReadUInt32();
            if (BugcheckNames.ContainsKey(bc2))
            {
                bugcheck = bc2;
                br.ReadUInt32();
                p1 = br.ReadUInt64();
                p2 = br.ReadUInt64();
                p3 = br.ReadUInt64();
                p4 = br.ReadUInt64();
            }
        }

        if (!BugcheckNames.ContainsKey(bugcheck) && bugcheck > 0x10000)
        {
            fs.Seek(0, SeekOrigin.Begin);
            var header = br.ReadBytes(512);
            for (int i = 0; i < header.Length - 36; i += 4)
            {
                uint candidate = BitConverter.ToUInt32(header, i);
                if (BugcheckNames.ContainsKey(candidate) && candidate >= 0x0A)
                {
                    bugcheck = candidate;
                    int paramOff = i + 4;
                    uint maybepad = BitConverter.ToUInt32(header, paramOff);
                    if (maybepad == 0 && paramOff + 36 < header.Length)
                        paramOff += 4;
                    if (paramOff + 32 <= header.Length)
                    {
                        p1 = BitConverter.ToUInt64(header, paramOff);
                        p2 = BitConverter.ToUInt64(header, paramOff + 8);
                        p3 = BitConverter.ToUInt64(header, paramOff + 16);
                        p4 = BitConverter.ToUInt64(header, paramOff + 24);
                    }
                    break;
                }
            }
        }

        var fileTime = File.GetLastWriteTime(path);
        string bcName = BugcheckNames.TryGetValue(bugcheck, out var n) ? n : $"0x{bugcheck:X8}";
        bool isGpu = bugcheck == 0x116 || bugcheck == 0x117 || bugcheck == 0x119;

        return new DumpInfo(Path.GetFileName(path), fileTime, bugcheck, bcName,
            p1, p2, p3, p4, isGpu);
    }

    // 32-bit kernel dump header:
    // Offset 0x00: "PAGE" signature
    // Offset 0x20: BugCheckCode (4 bytes)
    // Offset 0x24: BugCheckParameter1-4 (4 bytes each)
    static DumpInfo? ParseKernelDump32(string path, FileStream fs, BinaryReader br)
    {
        fs.Seek(0x20, SeekOrigin.Begin);
        uint bugcheck = br.ReadUInt32();
        ulong p1 = br.ReadUInt32();
        ulong p2 = br.ReadUInt32();
        ulong p3 = br.ReadUInt32();
        ulong p4 = br.ReadUInt32();

        var fileTime = File.GetLastWriteTime(path);
        string bcName = BugcheckNames.TryGetValue(bugcheck, out var n) ? n : $"0x{bugcheck:X8}";
        bool isGpu = bugcheck == 0x116 || bugcheck == 0x117 || bugcheck == 0x119;

        return new DumpInfo(Path.GetFileName(path), fileTime, bugcheck, bcName,
            p1, p2, p3, p4, isGpu);
    }

    static DumpInfo? ParseMdmp(string path, FileStream fs, BinaryReader br)
    {
        ushort version = br.ReadUInt16();
        ushort implVersion = br.ReadUInt16();
        uint streamCount = br.ReadUInt32();
        uint streamDirRva = br.ReadUInt32();
        br.ReadUInt32();
        uint timeDateStamp = br.ReadUInt32();

        var timestamp = DateTimeOffset.FromUnixTimeSeconds(timeDateStamp).LocalDateTime;

        fs.Seek(streamDirRva, SeekOrigin.Begin);
        uint bugcheck = 0;
        ulong p1 = 0, p2 = 0, p3 = 0, p4 = 0;

        for (int i = 0; i < streamCount; i++)
        {
            uint sType = br.ReadUInt32();
            uint sSize = br.ReadUInt32();
            uint sRva = br.ReadUInt32();

            if (sType == 6 && sSize > 0)
            {
                long saved = fs.Position;
                fs.Seek(sRva, SeekOrigin.Begin);
                br.ReadUInt32();
                br.ReadUInt32();
                bugcheck = br.ReadUInt32();
                br.ReadUInt32();
                br.ReadUInt64();
                br.ReadUInt64();
                uint numParams = br.ReadUInt32();
                br.ReadUInt32();
                if (numParams > 0) p1 = br.ReadUInt64();
                if (numParams > 1) p2 = br.ReadUInt64();
                if (numParams > 2) p3 = br.ReadUInt64();
                if (numParams > 3) p4 = br.ReadUInt64();
                fs.Seek(saved, SeekOrigin.Begin);
            }
        }

        string bcName = BugcheckNames.TryGetValue(bugcheck, out var n) ? n : $"0x{bugcheck:X8}";
        bool isGpu = bugcheck == 0x116 || bugcheck == 0x117 || bugcheck == 0x119;

        return new DumpInfo(Path.GetFileName(path), timestamp, bugcheck, bcName,
            p1, p2, p3, p4, isGpu);
    }

    public static string? FindCdb(Action<string>? log = null)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("where", "cdb")
            {
                RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
            };
            var proc = System.Diagnostics.Process.Start(psi);
            var output = proc?.StandardOutput.ReadToEnd().Trim();
            proc?.WaitForExit();
            if (proc?.ExitCode == 0 && !string.IsNullOrEmpty(output))
                return output.Split('\n')[0].Trim();
        }
        catch (Exception ex)
        {
            log?.Invoke($"Warning: 'where cdb' failed: {ex.Message}");
        }

        string[] searchPaths = [
            @"C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\cdb.exe",
            @"C:\Program Files\Windows Kits\10\Debuggers\x64\cdb.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\WindowsApps\cdb.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\WinDbg\cdb.exe"),
        ];

        foreach (var p in searchPaths)
            if (File.Exists(p)) return p;

        try
        {
            var appsDir = @"C:\Program Files\WindowsApps";
            if (Directory.Exists(appsDir))
            {
                foreach (var dir in Directory.GetDirectories(appsDir, "*WinDbg*"))
                {
                    var cdb = Path.Combine(dir, "amd64", "cdb.exe");
                    if (File.Exists(cdb)) return cdb;
                    cdb = Path.Combine(dir, "cdb.exe");
                    if (File.Exists(cdb)) return cdb;
                }
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"Warning: Could not enumerate WindowsApps: {ex.Message}");
        }

        return null;
    }

    static string? RunCdbAnalysis(string cdbPath, string dumpPath, Action<string>? log = null, System.Threading.CancellationToken ct = default)
    {
        // Combine cancellation token with a 30s per-dump timeout so Cancel
        // interrupts within milliseconds while still bounding a hung cdb.
        using var timeoutCts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var linked = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        try
        {
            return ProcessRunner.RunAsync(cdbPath, linked.Token,
                "-z", $"\"{dumpPath}\"", "-c", "\"!analyze -v; q\"").GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            log?.Invoke($"  cdb analysis timed out after 30s: {Path.GetFileName(dumpPath)}");
            return null;
        }
        catch (Exception ex)
        {
            log?.Invoke($"  cdb analysis failed: {ex.Message}");
            return null;
        }
    }

    static string? ExtractCdbSummary(string cdbOutput)
    {
        var sb = new StringBuilder();
        var lines = cdbOutput.Split('\n');
        bool inStack = false;
        int stackLines = 0;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();

            if (line.Contains("BUGCHECK_STR:") || line.Contains("DEFAULT_BUCKET_ID:") ||
                line.Contains("PROCESS_NAME:") || line.Contains("IMAGE_NAME:") ||
                line.Contains("MODULE_NAME:") || line.Contains("FAULTING_MODULE:") ||
                line.Contains("FAILURE_BUCKET_ID:") || line.Contains("DRIVER_VERIFIER_IOMANAGER_VIOLATION"))
            {
                sb.AppendLine($"    {line.Trim()}");
            }

            if (line.Contains("STACK_TEXT:"))
            {
                inStack = true;
                sb.AppendLine("    STACK_TEXT (top frames):");
                stackLines = 0;
                continue;
            }

            if (inStack)
            {
                if (string.IsNullOrWhiteSpace(line) || stackLines >= 10)
                {
                    inStack = false;
                    continue;
                }
                sb.AppendLine($"      {line.Trim()}");
                stackLines++;
            }
        }

        var result = sb.ToString();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    public static string GenerateDumpReport(string dumpDir, bool forceDeepAnalysis = false, Action<string>? log = null, System.Threading.CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        var dumpFiles = Directory.GetFiles(dumpDir, "*.dmp");

        if (dumpFiles.Length == 0)
        {
            sb.AppendLine("  No minidump files found.");
            return sb.ToString();
        }

        string? cdbPath = FindCdb(log);
        bool deepAnalysis = forceDeepAnalysis && cdbPath != null;
        if (deepAnalysis)
            sb.AppendLine($"  Deep analysis enabled (cdb: {cdbPath})");
        else if (forceDeepAnalysis)
        {
            sb.AppendLine("  Deep analysis requested but cdb.exe not found.");
            sb.AppendLine("  Install WinDbg: winget install Microsoft.WinDbg");
        }

        sb.AppendLine($"  Analyzed {dumpFiles.Length} crash dump(s):");
        sb.AppendLine();

        int gpuCrashes = 0;
        foreach (var dmp in dumpFiles.OrderByDescending(f => new FileInfo(f).LastWriteTime))
        {
            ct.ThrowIfCancellationRequested();
            var info = AnalyzeDump(dmp, log);
            if (info == null)
            {
                sb.AppendLine($"  {Path.GetFileName(dmp)}: Could not parse");
                continue;
            }

            if (info.IsGpuRelated) gpuCrashes++;

            sb.AppendLine($"  {info.FileName}");
            sb.AppendLine($"    Date:       {info.Timestamp:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"    Bugcheck:   0x{info.BugcheckCode:X8} ({info.BugcheckName})");
            sb.AppendLine($"    Parameters: 0x{info.Param1:X} 0x{info.Param2:X} 0x{info.Param3:X} 0x{info.Param4:X}");
            if (info.IsGpuRelated)
                sb.AppendLine($"    >>> GPU-RELATED CRASH <<<");

            if (deepAnalysis && cdbPath != null)
            {
                log?.Invoke($"  Analyzing {info.FileName} with cdb...");
                var cdbOutput = RunCdbAnalysis(cdbPath, dmp, log, ct);
                if (cdbOutput != null)
                {
                    var summary = ExtractCdbSummary(cdbOutput);
                    if (summary != null)
                    {
                        sb.AppendLine("    --- WinDbg Analysis ---");
                        sb.Append(summary);
                        sb.AppendLine("    -----------------------");
                    }
                    log?.Invoke($"  {info.FileName}: cdb analysis done");
                }
                else
                {
                    log?.Invoke($"  {info.FileName}: cdb analysis failed");
                }
            }

            sb.AppendLine();
        }

        sb.AppendLine($"  GPU-related crashes: {gpuCrashes} of {dumpFiles.Length}");

        return sb.ToString();
    }
}
