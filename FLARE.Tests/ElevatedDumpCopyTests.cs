using FLARE.Core;

namespace FLARE.Tests;

// Staging-path validation is the invariant that keeps the elevated helper
// from becoming a SYSTEM-readable file exfiltrator driven by attacker argv.
public class ElevatedDumpCopyTests
{
    [Fact]
    public void GetStagingRoot_LivesUnderDoNotShare()
    {
        var stagingRoot = ElevatedDumpCopy.GetStagingRoot();

        Assert.StartsWith(FlareStorage.DoNotShareRoot(), stagingRoot, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DO_NOT_SHARE", stagingRoot, StringComparison.Ordinal);
    }

    [Fact]
    public void RunHelperMode_StagingOutsideLocalAppDataFlareRoot_RefusedWithDistinctExitCode()
    {
        var untrusted = Path.Combine(Path.GetTempPath(), $"flare_helper_reject_{Guid.NewGuid():N}");

        var result = ElevatedDumpCopy.RunHelperMode(untrusted);

        Assert.Equal(ElevatedDumpCopy.ExitStagingOutsideRoot, result);
        Assert.False(Directory.Exists(untrusted),
            "refused invocation must not create the staging directory");
    }

    [Fact]
    public void RunHelperMode_EmptyStagingPath_RefusedAsInvalid()
    {
        Assert.Equal(ElevatedDumpCopy.ExitInvalidStagingPath,
            ElevatedDumpCopy.RunHelperMode(""));
        Assert.Equal(ElevatedDumpCopy.ExitInvalidStagingPath,
            ElevatedDumpCopy.RunHelperMode("   "));
    }

    [Fact]
    public void RunHelperMode_TraversalFromLocalAppDataRoot_Refused()
    {
        // Path.GetFullPath collapses "..\" so the prefix match sees the real target.
        var escaped = Path.Combine(ElevatedDumpCopy.GetStagingRoot(), "..", "evil");

        Assert.Equal(ElevatedDumpCopy.ExitStagingOutsideRoot,
            ElevatedDumpCopy.RunHelperMode(escaped));
    }

    [Fact]
    public void RunHelperMode_SiblingPrefixNotAccepted()
    {
        // Trailing-separator boundary — "FLAREEvil\" must not match "FLARE\".
        var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var sibling = Path.Combine(localApp, "FLAREEvil", "staging");

        Assert.Equal(ElevatedDumpCopy.ExitStagingOutsideRoot,
            ElevatedDumpCopy.RunHelperMode(sibling));
    }

    [Fact]
    public void RunHelperMode_RelativePath_RejectedAsOutsideRoot()
    {
        // Relative resolves against the helper's CWD (System32 under runas).
        Assert.Equal(ElevatedDumpCopy.ExitStagingOutsideRoot,
            ElevatedDumpCopy.RunHelperMode(@"FLARE\staging-evil"));
    }

    [Fact]
    public void RunHelperMode_ForwardSlashOutsideRoot_Rejected()
    {
        Assert.Equal(ElevatedDumpCopy.ExitStagingOutsideRoot,
            ElevatedDumpCopy.RunHelperMode("C:/Temp/flare-evil"));
    }

    [Fact]
    public void RunHelperMode_ValidPathUnderRoot_PassesValidation()
    {
        // The copy step may fail downstream (unelevated can't read
        // %SystemRoot%\Minidump) — asserting only that validation passed.
        var staging = Path.Combine(ElevatedDumpCopy.GetStagingRoot(), $"staging-validate-{Guid.NewGuid():N}");

        var result = ElevatedDumpCopy.RunHelperMode(staging);

        Assert.NotEqual(ElevatedDumpCopy.ExitStagingOutsideRoot, result);
        Assert.NotEqual(ElevatedDumpCopy.ExitInvalidStagingPath, result);
        Assert.NotEqual(ElevatedDumpCopy.ExitStagingReparsePoint, result);

        try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
    }

    [Fact]
    public void HasReparsePointInExistingPath_ReparseRoot_ReturnsTrue()
    {
        var root = Path.Combine(Path.GetTempPath(), "flare-reparse-root");
        var staging = Path.Combine(root, "staging");

        var result = ElevatedDumpCopy.HasReparsePointInExistingPath(
            staging,
            root,
            p => string.Equals(Path.GetFullPath(p), Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase),
            _ => FileAttributes.Directory | FileAttributes.ReparsePoint);

        Assert.True(result);
    }

    [Fact]
    public void HasReparsePointInExistingPath_ReparseChild_ReturnsTrue()
    {
        var root = Path.Combine(Path.GetTempPath(), "flare-reparse-child");
        var staging = Path.Combine(root, "staging");

        var result = ElevatedDumpCopy.HasReparsePointInExistingPath(
            staging,
            root,
            p => string.Equals(Path.GetFullPath(p), Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(Path.GetFullPath(p), Path.GetFullPath(staging), StringComparison.OrdinalIgnoreCase),
            p => string.Equals(Path.GetFullPath(p), Path.GetFullPath(staging), StringComparison.OrdinalIgnoreCase)
                ? FileAttributes.Directory | FileAttributes.ReparsePoint
                : FileAttributes.Directory);

        Assert.True(result);
    }

    [Fact]
    public void HasReparsePointInExistingPath_NormalExistingRootAndMissingChild_ReturnsFalse()
    {
        var root = Path.Combine(Path.GetTempPath(), "flare-normal-root");
        var staging = Path.Combine(root, "staging");

        var result = ElevatedDumpCopy.HasReparsePointInExistingPath(
            staging,
            root,
            p => string.Equals(Path.GetFullPath(p), Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase),
            _ => FileAttributes.Directory);

        Assert.False(result);
    }

    [Fact]
    public void HasReparsePointInExistingPath_PathOutsideRoot_ReturnsTrue()
    {
        var root = Path.Combine(Path.GetTempPath(), "flare-root");
        var outside = Path.Combine(Path.GetTempPath(), "flare-outside", "staging");

        var result = ElevatedDumpCopy.HasReparsePointInExistingPath(
            outside,
            root,
            _ => false,
            _ => FileAttributes.Directory);

        Assert.True(result);
    }

    [Fact]
    public void ReadCopyFailureSentinel_PresentWithCount_RecordsFailureAndDeletesSentinel()
    {
        var staging = Path.Combine(Path.GetTempPath(), $"flare_sentinel_{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        var sentinel = Path.Combine(staging, ElevatedDumpCopy.CopyFailureSentinelName);
        File.WriteAllText(sentinel, "3");
        try
        {
            var logs = new List<string>();
            var health = new CollectorHealth();

            ElevatedDumpCopy.ReadCopyFailureSentinel(staging, logs.Add, health);

            Assert.Contains(health.Notices, n =>
                n.Kind == CollectorNoticeKind.Failure &&
                n.Source == "minidump copy" &&
                n.Message.Contains("3 dump(s)"));
            Assert.Contains(logs, l => l.Contains("3 dump(s)"));
            Assert.False(File.Exists(sentinel), "sentinel must be deleted after being read");
        }
        finally
        {
            try { Directory.Delete(staging, true); } catch { }
        }
    }

    [Fact]
    public void ReadCopyFailureSentinel_Absent_DoesNothing()
    {
        var staging = Path.Combine(Path.GetTempPath(), $"flare_sentinel_{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            var health = new CollectorHealth();

            ElevatedDumpCopy.ReadCopyFailureSentinel(staging, null, health);

            Assert.Empty(health.Notices);
        }
        finally
        {
            try { Directory.Delete(staging, true); } catch { }
        }
    }

    [Fact]
    public void ReadCopyFailureSentinel_ZeroCount_RecordsNothing()
    {
        var staging = Path.Combine(Path.GetTempPath(), $"flare_sentinel_{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        var sentinel = Path.Combine(staging, ElevatedDumpCopy.CopyFailureSentinelName);
        File.WriteAllText(sentinel, "0");
        try
        {
            var health = new CollectorHealth();

            ElevatedDumpCopy.ReadCopyFailureSentinel(staging, null, health);

            Assert.Empty(health.Notices);
            Assert.False(File.Exists(sentinel));
        }
        finally
        {
            try { Directory.Delete(staging, true); } catch { }
        }
    }

    [Fact]
    public void TryWriteCopyFailureSentinel_FreshDir_WritesCount()
    {
        var staging = Path.Combine(Path.GetTempPath(), $"flare_sentinel_write_{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            Assert.True(ElevatedDumpCopy.TryWriteCopyFailureSentinel(staging, 7));

            var sentinel = Path.Combine(staging, ElevatedDumpCopy.CopyFailureSentinelName);
            Assert.Equal("7", File.ReadAllText(sentinel));
        }
        finally
        {
            try { Directory.Delete(staging, true); } catch { }
        }
    }

    [Fact]
    public void TryWriteCopyFailureSentinel_PrePlantedSentinel_RefusesWrite()
    {
        var staging = Path.Combine(Path.GetTempPath(), $"flare_sentinel_preplant_{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        var sentinel = Path.Combine(staging, ElevatedDumpCopy.CopyFailureSentinelName);
        File.WriteAllText(sentinel, "attacker-payload");
        try
        {
            Assert.False(ElevatedDumpCopy.TryWriteCopyFailureSentinel(staging, 3));

            Assert.Equal("attacker-payload", File.ReadAllText(sentinel));
        }
        finally
        {
            try { Directory.Delete(staging, true); } catch { }
        }
    }

    [Fact]
    public void ReadCopyFailureSentinel_GarbageContents_DoesNotThrowOrRecord()
    {
        var staging = Path.Combine(Path.GetTempPath(), $"flare_sentinel_{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        var sentinel = Path.Combine(staging, ElevatedDumpCopy.CopyFailureSentinelName);
        File.WriteAllText(sentinel, "not-a-number");
        try
        {
            var health = new CollectorHealth();

            ElevatedDumpCopy.ReadCopyFailureSentinel(staging, null, health);

            Assert.Empty(health.Notices);
        }
        finally
        {
            try { Directory.Delete(staging, true); } catch { }
        }
    }

    [Fact]
    public void SweepStaleStagings_DeletesOldStagingSubdirs()
    {
        var root = Path.Combine(Path.GetTempPath(), $"flare_sweep_root_{Guid.NewGuid():N}");
        var stale = Path.Combine(root, "staging-stale");
        var fresh = Path.Combine(root, "staging-fresh");
        Directory.CreateDirectory(stale);
        Directory.CreateDirectory(fresh);
        Directory.SetLastWriteTimeUtc(stale, DateTime.UtcNow - ElevatedDumpCopy.StagingOrphanAge - TimeSpan.FromMinutes(5));
        try
        {
            ElevatedDumpCopy.SweepStaleStagings(root);

            Assert.False(Directory.Exists(stale), "stale staging dir should be swept");
            Assert.True(Directory.Exists(fresh), "fresh staging dir must be kept");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void SweepStaleStagings_IgnoresNonStagingSiblings()
    {
        var root = Path.Combine(Path.GetTempPath(), $"flare_sweep_root_{Guid.NewGuid():N}");
        var unrelated = Path.Combine(root, "Reports");
        Directory.CreateDirectory(unrelated);
        Directory.SetLastWriteTimeUtc(unrelated, DateTime.UtcNow - ElevatedDumpCopy.StagingOrphanAge - TimeSpan.FromMinutes(5));
        try
        {
            ElevatedDumpCopy.SweepStaleStagings(root);

            Assert.True(Directory.Exists(unrelated), "sweep must only touch staging-* subdirs");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void SweepStaleStagings_MissingRoot_DoesNotThrow()
    {
        var root = Path.Combine(Path.GetTempPath(), $"flare_sweep_missing_{Guid.NewGuid():N}");
        ElevatedDumpCopy.SweepStaleStagings(root);
    }
}
