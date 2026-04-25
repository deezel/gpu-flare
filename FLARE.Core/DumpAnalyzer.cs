using System;
using System.IO;
using System.Linq;
using System.Text;

namespace FLARE.Core;

public static class DumpAnalyzer
{
    // PAGEDU64 kernel dumps normally carry BugCheckCode at 0x38, followed by
    // 4 bytes of padding and the four 8-byte bugcheck parameters starting at
    // 0x40. Some older dump writers have been seen placing the bugcheck at
    // 0x40 instead, so this parser tries both documented layouts before using
    // the heuristic scan window as a last resort.
    private static class KernelDump64Layout
    {
        public const int BugcheckCodeOffset = 0x38;
        public const int AlternateBugcheckCodeOffset = 0x40;
        public const int HeuristicScanStartOffset = 0x30;
        public const int HeuristicScanEndOffset = 0x80;
    }

    private static class KernelDump32Layout
    {
        public const int BugcheckCodeOffset = 0x20;
    }

    private const uint MinimumHeuristicBugcheckCode = 0x0A;

    public record DumpInfo(
        string FileName,
        DateTime Timestamp,
        uint BugcheckCode,
        string BugcheckName,
        ulong Param1, ulong Param2, ulong Param3, ulong Param4,
        bool IsGpuRelated,
        bool IsUserModeException = false,
        bool IsHeuristicMatch = false
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
    static DumpInfo? ParseKernelDump64(string path, FileStream fs, BinaryReader br)
    {
        uint bugcheck = 0;
        ulong p1 = 0, p2 = 0, p3 = 0, p4 = 0;
        bool heuristic = false;

        fs.Seek(KernelDump64Layout.BugcheckCodeOffset, SeekOrigin.Begin);
        bugcheck = br.ReadUInt32();
        uint padding = br.ReadUInt32();
        p1 = br.ReadUInt64();
        p2 = br.ReadUInt64();
        p3 = br.ReadUInt64();
        p4 = br.ReadUInt64();

        if (!BugcheckCatalog.IsKnown(bugcheck) && bugcheck > 0x10000)
        {
            fs.Seek(KernelDump64Layout.AlternateBugcheckCodeOffset, SeekOrigin.Begin);
            var bc2 = br.ReadUInt32();
            if (BugcheckCatalog.IsKnown(bc2))
            {
                bugcheck = bc2;
                br.ReadUInt32();
                p1 = br.ReadUInt64();
                p2 = br.ReadUInt64();
                p3 = br.ReadUInt64();
                p4 = br.ReadUInt64();
            }
        }

        if (!BugcheckCatalog.IsKnown(bugcheck) && bugcheck > 0x10000)
        {
            fs.Seek(0, SeekOrigin.Begin);
            var header = br.ReadBytes(KernelDump64Layout.HeuristicScanEndOffset);
            for (int i = KernelDump64Layout.HeuristicScanStartOffset; i <= header.Length - 4; i += 4)
            {
                uint candidate = BitConverter.ToUInt32(header, i);
                if (BugcheckCatalog.IsKnown(candidate) && candidate >= MinimumHeuristicBugcheckCode)
                {
                    bugcheck = candidate;
                    heuristic = true;
                    int paramOff = i + 4;
                    if (paramOff + 32 <= header.Length)
                    {
                        uint maybepad = BitConverter.ToUInt32(header, paramOff);
                        if (maybepad == 0) paramOff += 4;
                    }
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
        string bcName = BugcheckCatalog.GetName(bugcheck);
        bool isGpu = BugcheckCatalog.IsGpuRelated(bugcheck);

        return new DumpInfo(Path.GetFileName(path), fileTime, bugcheck, bcName,
            p1, p2, p3, p4, isGpu, IsHeuristicMatch: heuristic);
    }
    static DumpInfo? ParseKernelDump32(string path, FileStream fs, BinaryReader br)
    {
        fs.Seek(KernelDump32Layout.BugcheckCodeOffset, SeekOrigin.Begin);
        uint bugcheck = br.ReadUInt32();
        ulong p1 = br.ReadUInt32();
        ulong p2 = br.ReadUInt32();
        ulong p3 = br.ReadUInt32();
        ulong p4 = br.ReadUInt32();

        var fileTime = File.GetLastWriteTime(path);
        string bcName = BugcheckCatalog.GetName(bugcheck);
        bool isGpu = BugcheckCatalog.IsGpuRelated(bugcheck);

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

        string bcName = BugcheckCatalog.GetName(bugcheck);
        bool isGpu = BugcheckCatalog.IsGpuRelated(bugcheck);
        bool userMode = bugcheck != 0 && !BugcheckCatalog.IsKnown(bugcheck);

        return new DumpInfo(Path.GetFileName(path), timestamp, bugcheck, bcName,
            p1, p2, p3, p4, isGpu, IsUserModeException: userMode);
    }

    public static string GenerateDumpReport(string dumpDir, bool forceDeepAnalysis = false, Action<string>? log = null, System.Threading.CancellationToken ct = default, DateTime? cutoff = null, CollectorHealth? health = null)
    {
        var sb = new StringBuilder();
        var allFiles = Directory.GetFiles(dumpDir, "*.dmp");
        var dumpFiles = cutoff.HasValue
            ? allFiles.Where(f => File.GetLastWriteTime(f) >= cutoff.Value).ToArray()
            : allFiles;

        if (dumpFiles.Length == 0)
        {
            if (allFiles.Length > 0 && cutoff.HasValue)
                sb.AppendLine($"  {allFiles.Length} dump(s) present but all older than cutoff {cutoff.Value:yyyy-MM-dd}; nothing to analyze.");
            else
                sb.AppendLine("  No minidump files found.");
            return sb.ToString();
        }

        string? cdbPath = CdbLocator.FindCdb(log);
        bool deepAnalysis = forceDeepAnalysis && cdbPath != null;
        if (deepAnalysis)
            sb.AppendLine($"  Deep analysis enabled (cdb: {cdbPath})");
        else if (forceDeepAnalysis)
        {
            sb.AppendLine("  Deep analysis requested but cdb.exe not found.");
            sb.AppendLine("  Install WinDbg: winget install Microsoft.WinDbg");
            health?.Failure("cdb.exe", "deep analysis requested but cdb.exe was not found under any Microsoft debugger root");
        }

        sb.AppendLine($"  Analyzed {dumpFiles.Length} crash dump(s):");
        sb.AppendLine();

        int gpuCrashes = 0;
        int heuristicGpuCrashes = 0;
        foreach (var dmp in dumpFiles.OrderByDescending(f => new FileInfo(f).LastWriteTime))
        {
            ct.ThrowIfCancellationRequested();
            var info = AnalyzeDump(dmp, log);
            if (info == null)
            {
                sb.AppendLine($"  {Path.GetFileName(dmp)}: Could not parse");
                continue;
            }

            if (info.IsGpuRelated)
            {
                gpuCrashes++;
                if (info.IsHeuristicMatch)
                    heuristicGpuCrashes++;
            }

            sb.AppendLine($"  {info.FileName}");
            sb.AppendLine($"    Date:       {info.Timestamp:yyyy-MM-dd HH:mm:ss}");
            if (info.IsUserModeException)
            {
                sb.AppendLine($"    Exception:  0x{info.BugcheckCode:X8} (user-mode dump — not a kernel bugcheck)");
            }
            else if (info.IsHeuristicMatch)
            {
                sb.AppendLine($"    Bugcheck:   0x{info.BugcheckCode:X8} ({info.BugcheckName}, heuristic match)");
            }
            else
            {
                sb.AppendLine($"    Bugcheck:   0x{info.BugcheckCode:X8} ({info.BugcheckName})");
            }
            if (info.IsHeuristicMatch)
                sb.AppendLine("    Parameters: (omitted — bugcheck located by heuristic scan, parameter offsets speculative)");
            else
                sb.AppendLine($"    Parameters: 0x{info.Param1:X} 0x{info.Param2:X} 0x{info.Param3:X} 0x{info.Param4:X}");
            if (info.IsGpuRelated)
                sb.AppendLine($"    >>> GPU-RELATED CRASH <<<");

            if (deepAnalysis && cdbPath != null)
            {
                var cdbOutput = CdbAnalysisCache.TryLoad(dmp, log);
                if (cdbOutput != null)
                {
                    log?.Invoke($"  {info.FileName}: using cached cdb analysis");
                }
                else
                {
                    log?.Invoke($"  Analyzing {info.FileName} with cdb...");
                    cdbOutput = CdbRunner.RunCdbAnalysis(cdbPath, dmp, log, ct);
                    if (cdbOutput != null)
                    {
                        CdbAnalysisCache.Store(dmp, cdbOutput, log);
                        log?.Invoke($"  {info.FileName}: cdb analysis done");
                    }
                    else
                    {
                        log?.Invoke($"  {info.FileName}: cdb analysis failed");
                        sb.AppendLine("    WinDbg Analysis: unavailable (cdb produced no usable output or timed out).");
                        health?.Failure($"cdb analysis: {info.FileName}", "cdb produced no usable output or timed out");
                    }
                }

                if (cdbOutput != null)
                {
                    var summary = CdbRunner.ExtractCdbSummary(cdbOutput, log, health);
                    if (summary != null)
                    {
                        sb.AppendLine("    --- WinDbg Analysis ---");
                        sb.Append(summary);
                        sb.AppendLine("    -----------------------");
                    }
                    else
                    {
                        sb.AppendLine("    WinDbg Analysis: no reportable summary could be extracted from cdb output.");
                        health?.Failure($"cdb summary: {info.FileName}", "cdb ran but FLARE could not extract a reportable summary");
                    }
                }
            }

            sb.AppendLine();
        }

        var heuristicSuffix = heuristicGpuCrashes > 0
            ? $" ({heuristicGpuCrashes} via heuristic match)"
            : "";
        sb.AppendLine($"  GPU-related crashes: {gpuCrashes} of {dumpFiles.Length}{heuristicSuffix}");

        return sb.ToString();
    }
}
