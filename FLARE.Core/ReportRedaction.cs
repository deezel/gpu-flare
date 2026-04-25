using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FLARE.Core;

// Pattern-based: any line carrying a UUID is scrubbed regardless of pipeline ordering,
// without needing the GPU collector to run first.
public static partial class ReportRedaction
{
    internal const string RedactedMark = "[redacted]";

    [GeneratedRegex(@"GPU-[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}",
        RegexOptions.None, matchTimeoutMilliseconds: 500)]
    private static partial Regex GpuUuidRegex();

    internal const string UserProfileMark = "%USERPROFILE%";

    [GeneratedRegex(@"[A-Za-z]:[\\/]Users[\\/][^\\/""\r\n]+(?=[\\/])",
        RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 500)]
    private static partial Regex UserPathWithSubpathRegex();

    [GeneratedRegex(@"(?<="")[A-Za-z]:[\\/]Users[\\/][^\\/""\r\n]+(?="")",
        RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 500)]
    private static partial Regex DoubleQuotedBareUserPathRegex();

    [GeneratedRegex(@"(?<=')[A-Za-z]:[\\/]Users[\\/][^\\/'\r\n]+(?=')",
        RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 500)]
    private static partial Regex SingleQuotedBareUserPathRegex();

    [GeneratedRegex(@"[A-Za-z]:[\\/]Users[\\/][^\\/""\r\n\s;]+(?=[""\r\n\s;]|$)",
        RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 500)]
    private static partial Regex UserPathBareRegex();

    private static readonly string[] UserScopedPathEnvVars =
    [
        "LOCALAPPDATA",
        "APPDATA",
        "TEMP",
        "TMP",
        "OneDrive",
        "OneDriveCommercial",
        "OneDriveConsumer",
    ];

    private sealed record PathReplacement(string Path, string Marker);

    internal static string RedactCdbSummary(string cdbSummary)
    {
        if (string.IsNullOrEmpty(cdbSummary)) return cdbSummary;
        // Keep parity with RedactLogMessage so "visible in log => visible in report"
        // never has a UUID gap (DeviceInstanceId strings inside nvlddmkm symbols).
        return RedactGpuUuids(ScrubUserPaths(cdbSummary));
    }

    public static string ScrubUserPaths(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var scrubbed = ScrubUserPaths(
            input,
            Environment.GetEnvironmentVariable,
            Environment.GetFolderPath);
        return ScrubGenericUserPaths(scrubbed);
    }

    internal static string ScrubUserPaths(string input, Func<string, string?> getEnvironmentVariable)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var scrubbed = ScrubUserPaths(input, getEnvironmentVariable, _ => "");
        return ScrubGenericUserPaths(scrubbed);
    }

    private static string ScrubUserPaths(
        string input,
        Func<string, string?> getEnvironmentVariable,
        Func<Environment.SpecialFolder, string> getSpecialFolderPath)
    {
        var replacements = GetUserPathReplacements(getEnvironmentVariable, getSpecialFolderPath);
        var scrubbed = ApplyPathReplacements(
            input,
            replacements.Where(r => r.Marker == UserProfileMark));
        scrubbed = ApplyPathReplacements(
            scrubbed,
            replacements.Where(r => r.Marker != UserProfileMark));
        return ScrubCurrentUsernameProfile(scrubbed, getEnvironmentVariable);
    }

    internal static string ScrubUserPaths(string input, string? currentUserProfile)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var scrubbed = ScrubKnownProfile(input, currentUserProfile);
        return ScrubGenericUserPaths(scrubbed);
    }

    static string ScrubGenericUserPaths(string input)
    {
        var scrubbed = input;
        scrubbed = UserPathWithSubpathRegex().Replace(scrubbed, UserProfileMark);
        scrubbed = DoubleQuotedBareUserPathRegex().Replace(scrubbed, UserProfileMark);
        scrubbed = SingleQuotedBareUserPathRegex().Replace(scrubbed, UserProfileMark);
        return UserPathBareRegex().Replace(scrubbed, UserProfileMark);
    }

    static List<PathReplacement> GetUserPathReplacements(
        Func<string, string?> getEnvironmentVariable,
        Func<Environment.SpecialFolder, string> getSpecialFolderPath)
    {
        var replacements = new List<PathReplacement>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? path, string marker)
        {
            var normalized = NormalizePathRoot(path);
            if (normalized == null) return;
            if (!seen.Add(normalized)) return;
            replacements.Add(new PathReplacement(normalized, marker));
        }

        Add(getEnvironmentVariable("USERPROFILE"), UserProfileMark);
        Add(getEnvironmentVariable("HOME"), UserProfileMark);

        var homeDrive = getEnvironmentVariable("HOMEDRIVE");
        var homePath = getEnvironmentVariable("HOMEPATH");
        if (!string.IsNullOrWhiteSpace(homeDrive) && !string.IsNullOrWhiteSpace(homePath))
            Add(homeDrive + homePath, UserProfileMark);

        Add(getSpecialFolderPath(Environment.SpecialFolder.UserProfile), UserProfileMark);

        foreach (var envVar in UserScopedPathEnvVars)
            Add(getEnvironmentVariable(envVar), $"%{envVar}%");

        return replacements
            .OrderByDescending(r => r.Path.Length)
            .ToList();
    }

    static string? NormalizePathRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var trimmed = path.Trim().Trim('"').TrimEnd('\\', '/');
        if (trimmed.Length == 0 || trimmed.Contains('%') || trimmed.Contains(';'))
            return null;

        try
        {
            if (!Path.IsPathFullyQualified(trimmed)) return null;

            var full = Path.GetFullPath(trimmed).TrimEnd('\\', '/');
            var root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root)) return null;

            var rootTrimmed = root.TrimEnd('\\', '/');
            if (full.Equals(rootTrimmed, StringComparison.OrdinalIgnoreCase))
                return null;

            return full;
        }
        catch
        {
            return null;
        }
    }

    static string ApplyPathReplacements(string input, IEnumerable<PathReplacement> replacements)
    {
        var scrubbed = input;
        foreach (var replacement in replacements)
            scrubbed = ReplacePathPrefix(scrubbed, replacement.Path, replacement.Marker);
        return scrubbed;
    }

    static string ScrubCurrentUsernameProfile(string input, Func<string, string?> getEnvironmentVariable)
    {
        var username = getEnvironmentVariable("USERNAME");
        if (string.IsNullOrWhiteSpace(username)) return input;

        var profilePattern = @"[A-Za-z]:[\\/]Users[\\/]" + Regex.Escape(username);
        return ReplaceUserProfilePattern(input, profilePattern, UserProfileMark);
    }

    static string ScrubKnownProfile(string input, string? currentUserProfile)
    {
        var normalized = NormalizePathRoot(currentUserProfile);
        return normalized == null
            ? input
            : ReplacePathPrefix(input, normalized, UserProfileMark);
    }

    static string ReplacePathPrefix(string input, string path, string marker)
    {
        var pattern = PathToSeparatorTolerantPattern(path);
        return ReplaceUserProfilePattern(input, pattern, marker);
    }

    static string PathToSeparatorTolerantPattern(string path)
    {
        var sb = new StringBuilder(path.Length * 2);
        foreach (var ch in path)
        {
            if (ch is '\\' or '/')
                sb.Append(@"[\\/]");
            else
                sb.Append(Regex.Escape(ch.ToString()));
        }
        return sb.ToString();
    }

    static string ReplaceUserProfilePattern(string input, string profilePattern, string marker)
    {
        var profileRegex = new Regex(profilePattern + @"(?=$|[\\/""'\r\n\s;])",
            RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(500));

        return profileRegex.Replace(input, marker);
    }

    public static string RedactAll(string input, string machineName)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var r = ScrubUserPaths(input);
        r = RedactGpuUuids(r);
        return RedactMachineName(r, machineName);
    }

    internal static string RedactGpuUuids(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        return GpuUuidRegex().Replace(input, RedactedMark);
    }

    // Word-boundary match so a machine named "DEV" doesn't scrub "DEVICE" or "DEVELOPMENT"
    // in the visible log. Length guard at 2: a 1-char name would match every standalone
    // letter in ordinary prose (e.g. "A" in "Press A to continue") — better to leak a
    // pathologically short hostname than to mangle every log line.
    internal static string RedactMachineName(string input, string machineName)
    {
        if (string.IsNullOrEmpty(input)) return input;
        if (string.IsNullOrWhiteSpace(machineName) || machineName.Length < 2) return input;

        var pattern = @"\b" + Regex.Escape(machineName) + @"\b";
        return Regex.Replace(
            input, pattern, RedactedMark,
            RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(500));
    }
}
