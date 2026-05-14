using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace FLARE.Core;

public enum DumpSection
{
    CrashDumps,
    LiveKernel,
}

public sealed class CdbDetailsSink
{
    public const string DumpsFilenamePlaceholder = "__FLARE_DUMPS_FILENAME__";
    public const string MainFilenamePlaceholder = "__FLARE_MAIN_FILENAME__";

    private static readonly HashSet<string> InlineFieldNames = new(StringComparer.Ordinal)
    {
        "BUGCHECK_STR",
        "PROCESS_NAME",
        "MODULE_NAME",
        "IMAGE_NAME",
        "FAULTING_MODULE",
        "FAILURE_BUCKET_ID",
        "DEFAULT_BUCKET_ID",
        "SYMBOL_NAME",
    };

    private static readonly Regex FieldLineRegex =
        new(@"^\s*(?<name>[A-Z_]+)\s*:\s*(?<value>.*?)\s*$", RegexOptions.Compiled, TimeSpan.FromMilliseconds(500));

    private readonly StringBuilder _crashDumps = new();
    private readonly StringBuilder _liveKernel = new();

    public bool HasContent => _crashDumps.Length > 0 || _liveKernel.Length > 0;

    public string EmitInlineAndArchive(DumpSection section, string dumpHeader, string fullCdbSummary)
    {
        var inline = new StringBuilder();
        foreach (var rawLine in fullCdbSummary.Split('\n'))
        {
            var match = FieldLineRegex.Match(rawLine.TrimEnd());
            if (!match.Success) continue;
            var name = match.Groups["name"].Value;
            if (!InlineFieldNames.Contains(name)) continue;
            var value = match.Groups["value"].Value;
            inline.AppendLine($"- **{name}:** `{value}`");
        }

        var target = section == DumpSection.CrashDumps ? _crashDumps : _liveKernel;
        target.AppendLine($"### {dumpHeader}");
        target.AppendLine();
        target.AppendLine("```text");
        target.Append(fullCdbSummary);
        if (fullCdbSummary.Length > 0 && fullCdbSummary[^1] != '\n')
            target.AppendLine();
        target.AppendLine("```");
        target.AppendLine();

        return inline.ToString();
    }

    public string? BuildDetailsBody(DateTime timestamp, bool redactIdentifiers, string machineName)
    {
        if (!HasContent) return null;

        var sb = new StringBuilder();
        sb.AppendLine("# GPU Error Analysis Report — Dump Details");
        sb.AppendLine();
        sb.AppendLine($"Generated: {timestamp:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Companion to: `{MainFilenamePlaceholder}`");
        sb.AppendLine();

        if (_crashDumps.Length > 0)
        {
            sb.AppendLine("## CRASH DUMP ANALYSIS");
            sb.AppendLine();
            sb.Append(_crashDumps);
        }

        if (_liveKernel.Length > 0)
        {
            sb.AppendLine("## LIVE KERNEL DUMP ANALYSIS");
            sb.AppendLine();
            sb.Append(_liveKernel);
        }

        var body = sb.ToString();
        return redactIdentifiers
            ? ReportRedaction.RedactAll(body, machineName)
            : body;
    }
}
