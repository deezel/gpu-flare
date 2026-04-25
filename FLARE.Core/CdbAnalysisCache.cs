using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FLARE.Core;

// Raw cdb transcripts are cached (not the extracted summary) so extractor tweaks
// apply retroactively. Key = dump path + size + mtime + CacheVersion.
internal static class CdbAnalysisCache
{
    // Bump to invalidate every existing cache file without walking and deleting them.
    internal const string CacheVersion = "v1";

    private const string VersionHeader = "# FLARE cdb cache ";
    private const string DumpHeader = "# dump: ";
    private const string SizeHeader = "# size: ";
    private const string MtimeHeader = "# mtime: ";
    private const string EndOfHeader = "#";
    private const string TrailerSentinel = "# end " + CacheVersion;
    private const int HeaderLineCount = 5;

    internal static string DefaultCacheRoot() => FlareStorage.CdbCacheDir();

    internal static string CacheFilePath(string dumpPath, string? cacheRoot = null)
    {
        var root = cacheRoot ?? DefaultCacheRoot();
        var fullPath = Path.GetFullPath(dumpPath).ToUpperInvariant();
        // 128-bit prefix: size+mtime on TryLoad is the only other guard against a collision.
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullPath)))
            .Substring(0, 32)
            .ToLowerInvariant();
        return Path.Combine(root, $"{Path.GetFileName(dumpPath)}.{hash}.cdb.txt");
    }

    public static string? TryLoad(string dumpPath, Action<string>? log = null, string? cacheRoot = null)
    {
        try
        {
            var cacheFile = CacheFilePath(dumpPath, cacheRoot);
            if (!File.Exists(cacheFile) || !File.Exists(dumpPath)) return null;

            var dumpInfo = new FileInfo(dumpPath);
            var lines = File.ReadAllLines(cacheFile);
            if (lines.Length < HeaderLineCount) return null;

            if (lines[0] != $"{VersionHeader}{CacheVersion}") return null;
            if (!lines[1].StartsWith(DumpHeader, StringComparison.Ordinal)) return null;
            if (!lines[2].StartsWith(SizeHeader, StringComparison.Ordinal)) return null;
            if (!lines[3].StartsWith(MtimeHeader, StringComparison.Ordinal)) return null;
            if (lines[4] != EndOfHeader) return null;

            if (!long.TryParse(lines[2][SizeHeader.Length..], out var cachedSize)) return null;
            if (cachedSize != dumpInfo.Length) return null;

            var cachedMtime = lines[3][MtimeHeader.Length..];
            if (cachedMtime != FormatMtime(dumpInfo.LastWriteTimeUtc)) return null;

            // Trailer guards against a crash/kill between header flush and body completion —
            // without it, a truncated transcript would pass size/mtime checks (those describe
            // the dump, not the cache file) and feed partial stack frames to the summary.
            if (lines[^1] != TrailerSentinel) return null;

            return string.Join('\n', lines, HeaderLineCount, lines.Length - HeaderLineCount - 1);
        }
        catch (Exception ex)
        {
            log?.Invoke($"  cdb cache read failed ({Path.GetFileName(dumpPath)}): {ex.Message}");
            return null;
        }
    }

    public static void Store(string dumpPath, string cdbTranscript, Action<string>? log = null, string? cacheRoot = null)
    {
        string? tempPath = null;
        try
        {
            var cacheFile = CacheFilePath(dumpPath, cacheRoot);
            var dir = Path.GetDirectoryName(cacheFile);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var dumpInfo = new FileInfo(dumpPath);

            var sb = new StringBuilder();
            sb.Append(VersionHeader).AppendLine(CacheVersion);
            sb.Append(DumpHeader).AppendLine(Path.GetFileName(dumpPath));
            sb.Append(SizeHeader).AppendLine(dumpInfo.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            sb.Append(MtimeHeader).AppendLine(FormatMtime(dumpInfo.LastWriteTimeUtc));
            sb.AppendLine(EndOfHeader);
            if (cdbTranscript.Length > 0 && cdbTranscript[^1] != '\n')
                sb.Append(cdbTranscript).Append('\n');
            else
                sb.Append(cdbTranscript);
            sb.AppendLine(TrailerSentinel);

            tempPath = cacheFile + $".{Guid.NewGuid():N}.tmp";
            File.WriteAllText(tempPath, sb.ToString());
            if (File.Exists(cacheFile))
                File.Replace(tempPath, cacheFile, null, ignoreMetadataErrors: true);
            else
                File.Move(tempPath, cacheFile);
            tempPath = null;
        }
        catch (Exception ex)
        {
            log?.Invoke($"  cdb cache write failed ({Path.GetFileName(dumpPath)}): {ex.Message}");
        }
        finally
        {
            try
            {
                if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch { }
        }
    }

    // Invariant ISO-8601; cache validation is string-compare so culture must not leak in.
    private static string FormatMtime(DateTime utc) =>
        utc.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
}
