using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace FLARE.UI.Helpers;

public static class BuildBanner
{
    public const string ProductTitle = "FLARE - Fault Log Analysis & Reboot Examination";

    private const string DependencyPrefix = "Dependency.";

    public static IReadOnlyList<(string Name, string Version)> GetDependencies(Assembly entry)
    {
        var assemblies = new List<Assembly> { entry };
        foreach (var refName in entry.GetReferencedAssemblies())
        {
            if (refName.Name is null || !refName.Name.StartsWith("FLARE.", StringComparison.Ordinal))
                continue;
            try
            {
                assemblies.Add(Assembly.Load(refName));
            }
            catch
            {
            }
        }

        var attrs = assemblies.SelectMany(a => a.GetCustomAttributes<AssemblyMetadataAttribute>());
        return CollectDependencies(attrs);
    }

    internal static IReadOnlyList<(string Name, string Version)> CollectDependencies(
        IEnumerable<AssemblyMetadataAttribute> attributes)
    {
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in attributes)
        {
            if (a.Key is null || a.Value is null) continue;
            if (!a.Key.StartsWith(DependencyPrefix, StringComparison.Ordinal)) continue;
            var name = a.Key[DependencyPrefix.Length..];
            if (name.Length == 0) continue;
            if (!seen.ContainsKey(name))
                seen[name] = a.Value;
        }
        return seen
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();
    }

    public static string GetWindowTitle(Assembly asm)
    {
        var infoVer = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var isRelease = ReadIsRelease(asm);
        return Format(infoVer, isRelease);
    }

    public static string GetAboutVersionLine(Assembly asm)
    {
        var infoVer = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var isRelease = ReadIsRelease(asm);
        return FormatAboutVersionLine(infoVer, isRelease);
    }

    internal static string FormatAboutVersionLine(string informationalVersion, bool isRelease)
    {
        var (version, hash) = SplitVersion(informationalVersion);

        if (isRelease)
            return $"Version {version} · release";
        if (string.Equals(hash, "dev", StringComparison.OrdinalIgnoreCase))
            return $"Version {version} · local dev build";
        if (!string.IsNullOrEmpty(hash))
            return $"Version {version} ({hash}) · snapshot";
        return $"Version {version} · non-release build";
    }

    internal static string Format(string informationalVersion, bool isRelease)
    {
        var (version, hash) = SplitVersion(informationalVersion);

        if (isRelease)
            return $"FLARE {version} - Fault Log Analysis & Reboot Examination";

        if (string.Equals(hash, "dev", StringComparison.OrdinalIgnoreCase))
            return $"[DEV BUILD] FLARE {version}+dev - Fault Log Analysis & Reboot Examination";

        if (!string.IsNullOrEmpty(hash))
            return $"[SNAPSHOT] FLARE {version}+{hash} - Fault Log Analysis & Reboot Examination";

        return $"[NON-RELEASE] FLARE {version} - Fault Log Analysis & Reboot Examination";
    }

    static (string Version, string Hash) SplitVersion(string informationalVersion)
    {
        var plus = informationalVersion.IndexOf('+');
        return plus < 0
            ? (informationalVersion, "")
            : (informationalVersion[..plus], informationalVersion[(plus + 1)..]);
    }

    static bool ReadIsRelease(Assembly asm)
    {
        var meta = asm.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "FlareIsRelease");
        return meta?.Value != null && bool.TryParse(meta.Value, out var b) && b;
    }
}
