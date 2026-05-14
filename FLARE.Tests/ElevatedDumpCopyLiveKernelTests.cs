using FLARE.Core;

namespace FLARE.Tests;

public class ElevatedDumpCopyLiveKernelTests
{
    [Fact]
    public void CopySubtreeIntoStaging_PreservesCategorySubdirs()
    {
        var src = Path.Combine(Path.GetTempPath(), $"lk_src_{Guid.NewGuid():N}");
        var stagingLk = Path.Combine(Path.GetTempPath(), $"lk_stg_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(src, "WATCHDOG"));
            Directory.CreateDirectory(Path.Combine(src, "WATCHDOG4401"));
            File.WriteAllBytes(Path.Combine(src, "WATCHDOG",     "a.dmp"), new byte[16]);
            File.WriteAllBytes(Path.Combine(src, "WATCHDOG4401", "b.dmp"), new byte[16]);

            var failed = ElevatedDumpCopy.CopyLiveKernelSubtree(src, stagingLk, Path.GetTempPath());

            Assert.Equal(0, failed);
            Assert.True(File.Exists(Path.Combine(stagingLk, "WATCHDOG",     "a.dmp")));
            Assert.True(File.Exists(Path.Combine(stagingLk, "WATCHDOG4401", "b.dmp")));
        }
        finally
        {
            try { Directory.Delete(src, true); } catch { }
            try { Directory.Delete(stagingLk, true); } catch { }
        }
    }

    [Fact]
    public void CopySubtreeIntoStaging_NonexistentSource_NoOp()
    {
        var src = Path.Combine(Path.GetTempPath(), $"lk_missing_{Guid.NewGuid():N}");
        var stagingLk = Path.Combine(Path.GetTempPath(), $"lk_stg_{Guid.NewGuid():N}");

        var failed = ElevatedDumpCopy.CopyLiveKernelSubtree(src, stagingLk, Path.GetTempPath());

        Assert.Equal(0, failed);
        Assert.False(Directory.Exists(stagingLk));
    }

    [Fact]
    public void CopySubtreeIntoStaging_IgnoresNonDumpFiles()
    {
        var src = Path.Combine(Path.GetTempPath(), $"lk_src_{Guid.NewGuid():N}");
        var stagingLk = Path.Combine(Path.GetTempPath(), $"lk_stg_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(src, "WATCHDOG"));
            File.WriteAllBytes(Path.Combine(src, "WATCHDOG", "good.dmp"), new byte[16]);
            File.WriteAllText (Path.Combine(src, "WATCHDOG", "ignore.txt"), "noise");

            ElevatedDumpCopy.CopyLiveKernelSubtree(src, stagingLk, Path.GetTempPath());

            Assert.True (File.Exists(Path.Combine(stagingLk, "WATCHDOG", "good.dmp")));
            Assert.False(File.Exists(Path.Combine(stagingLk, "WATCHDOG", "ignore.txt")));
        }
        finally
        {
            try { Directory.Delete(src, true); } catch { }
            try { Directory.Delete(stagingLk, true); } catch { }
        }
    }

    [Fact]
    public void MoveLiveKernelOutOfStaging_PreservesCategoryAtDestination()
    {
        var staging = Path.Combine(Path.GetTempPath(), $"lk_pmv_{Guid.NewGuid():N}");
        var dest = Path.Combine(Path.GetTempPath(), $"lk_dst_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(staging, "WATCHDOG"));
            Directory.CreateDirectory(Path.Combine(staging, "WATCHDOG4401"));
            File.WriteAllBytes(Path.Combine(staging, "WATCHDOG",     "a.dmp"), new byte[16]);
            File.WriteAllBytes(Path.Combine(staging, "WATCHDOG4401", "b.dmp"), new byte[16]);

            var moved = ElevatedDumpCopy.MoveLiveKernelToDestination(staging, dest);

            Assert.Equal(2, moved.Count);
            Assert.True(File.Exists(Path.Combine(dest, "WATCHDOG",     "a.dmp")));
            Assert.True(File.Exists(Path.Combine(dest, "WATCHDOG4401", "b.dmp")));
            Assert.False(File.Exists(Path.Combine(staging, "WATCHDOG",     "a.dmp")));
            Assert.False(File.Exists(Path.Combine(staging, "WATCHDOG4401", "b.dmp")));
        }
        finally
        {
            try { Directory.Delete(staging, true); } catch { }
            try { Directory.Delete(dest,    true); } catch { }
        }
    }

    [Fact]
    public void MoveLiveKernelOutOfStaging_NonexistentStagingSubdir_ReturnsEmpty()
    {
        var staging = Path.Combine(Path.GetTempPath(), $"lk_none_{Guid.NewGuid():N}");
        var dest = Path.Combine(Path.GetTempPath(), $"lk_none_dst_{Guid.NewGuid():N}");

        var moved = ElevatedDumpCopy.MoveLiveKernelToDestination(staging, dest);

        Assert.Empty(moved);
    }

    [Fact]
    public void CopyLiveKernelSubtree_AlwaysRunsRegardlessOfMinidumpSource()
    {
        // Pins the RunHelperMode invariant: LiveKernel collection must not be
        // transitively gated on the system minidump dir existing. Regression
        // guard for a bug where an early return on missing %SystemRoot%\Minidump
        // silently skipped the LiveKernel block.
        var sourceRoot = Path.Combine(Path.GetTempPath(), $"lk_src_{Guid.NewGuid():N}");
        var stagingLk = Path.Combine(Path.GetTempPath(), $"lk_stg_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(sourceRoot, "WATCHDOG"));
            File.WriteAllBytes(Path.Combine(sourceRoot, "WATCHDOG", "test.dmp"), new byte[16]);
            Directory.CreateDirectory(stagingLk);

            var failed = ElevatedDumpCopy.CopyLiveKernelSubtree(sourceRoot, stagingLk, Path.GetTempPath());

            Assert.Equal(0, failed);
            Assert.True(File.Exists(Path.Combine(stagingLk, "WATCHDOG", "test.dmp")));
        }
        finally
        {
            try { Directory.Delete(sourceRoot, true); } catch { }
            try { Directory.Delete(stagingLk, true); } catch { }
        }
    }

    [Fact]
    public void ReadCopyFailureSentinel_NonZero_LogsCombinedSourceWording()
    {
        var staging = Path.Combine(Path.GetTempPath(), $"lk_sent_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(staging);
            ElevatedDumpCopy.TryWriteCopyFailureSentinel(staging, 3);
            var logs = new List<string>();
            var health = new CollectorHealth();

            ElevatedDumpCopy.ReadCopyFailureSentinel(staging, logs.Add, health);

            Assert.Contains(logs, l => l.Contains("3") && l.Contains("could not be copied"));
            var notice = Assert.Single(health.Notices);
            Assert.Equal(CollectorNoticeKind.Failure, notice.Kind);
        }
        finally
        {
            try { Directory.Delete(staging, true); } catch { }
        }
    }
}
