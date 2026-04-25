using FLARE.Core;

namespace FLARE.Tests;

// ExpandSystemVariables is the trust gate between a registry MinidumpDir value
// and the elevated helper's File.Copy source. An unelevated parent that seeds
// its own env block before ShellExecute verb=runas gets its env inherited into
// the elevated helper — so %SystemRoot% expansion must not pull from that env.
// These tests pin the whitelist and rejection behavior.
public class MinidumpLocatorTests
{
    [Fact]
    public void ExpandSystemVariables_SystemRootToken_ResolvesUnderWindowsDirectory()
    {
        var result = MinidumpLocator.ExpandSystemVariables(@"%SystemRoot%\Minidump");

        Assert.NotNull(result);
        Assert.DoesNotContain("%", result);
        // The resolved root must end in \Minidump and start at the Win32-resolved
        // Windows directory (System32's parent) — not whatever the process env
        // happened to claim %SystemRoot% was.
        var expectedPrefix = MinidumpLocator.ResolveWindowsDirectory();
        Assert.StartsWith(expectedPrefix, result);
        Assert.EndsWith(@"\Minidump", result);
    }

    [Fact]
    public void ExpandSystemVariables_WindirAlias_ResolvesToSameLocationAsSystemRoot()
    {
        var fromSystemRoot = MinidumpLocator.ExpandSystemVariables("%SystemRoot%");
        var fromWindir = MinidumpLocator.ExpandSystemVariables("%windir%");

        Assert.Equal(fromSystemRoot, fromWindir);
    }

    [Fact]
    public void ExpandSystemVariables_CaseInsensitiveTokenMatching()
    {
        var lower = MinidumpLocator.ExpandSystemVariables("%systemroot%");
        var upper = MinidumpLocator.ExpandSystemVariables("%SYSTEMROOT%");
        var mixed = MinidumpLocator.ExpandSystemVariables("%SystemRoot%");

        Assert.Equal(mixed, lower);
        Assert.Equal(mixed, upper);
    }

    [Fact]
    public void ExpandSystemVariables_LiteralAbsolutePath_ReturnedUnchanged()
    {
        var result = MinidumpLocator.ExpandSystemVariables(@"D:\Dumps\Kernel");

        Assert.Equal(@"D:\Dumps\Kernel", result);
    }

    [Fact]
    public void ExpandSystemVariables_UnknownVariable_ReturnsNull()
    {
        // A same-user attacker can set %USERPROFILE% (or any other env name) in
        // its process env before spawning the UAC prompt. That env is inherited
        // into the elevated helper, so the expander must refuse to substitute
        // anything not on its whitelist. Null tells the caller to fall back to
        // a trusted default instead.
        Assert.Null(MinidumpLocator.ExpandSystemVariables(@"%USERPROFILE%\Dumps"));
        Assert.Null(MinidumpLocator.ExpandSystemVariables(@"%TEMP%"));
        Assert.Null(MinidumpLocator.ExpandSystemVariables(@"%PATH%"));
    }

    [Fact]
    public void ExpandSystemVariables_MixedWhitelistedAndUnknown_StillReturnsNull()
    {
        // A single unknown token poisons the whole value — partial substitution
        // could still redirect the helper to an attacker-controlled location via
        // the unknown segment.
        Assert.Null(MinidumpLocator.ExpandSystemVariables(@"%SystemRoot%\%BOGUS%\Minidump"));
    }

    [Fact]
    public void ExpandSystemVariables_NoVariables_PassThrough()
    {
        var result = MinidumpLocator.ExpandSystemVariables(@"C:\Literal\Path");

        Assert.Equal(@"C:\Literal\Path", result);
    }

    [Fact]
    public void DefaultDumpDir_ResolvesUnderWindowsDirectory()
    {
        var def = MinidumpLocator.DefaultDumpDir();
        var windows = MinidumpLocator.ResolveWindowsDirectory();

        Assert.StartsWith(windows, def);
        Assert.EndsWith(@"\Minidump", def);
    }

    [Fact]
    public void IsUnderWindowsRoot_PathInsideSystemRoot_True()
    {
        var inside = Path.Combine(MinidumpLocator.ResolveWindowsDirectory(), "Minidump");
        Assert.True(MinidumpLocator.IsUnderWindowsRoot(inside));
    }

    [Fact]
    public void IsUnderWindowsRoot_PathOutsideSystemRoot_False()
    {
        Assert.False(MinidumpLocator.IsUnderWindowsRoot(@"D:\Dumps\Kernel"));
        Assert.False(MinidumpLocator.IsUnderWindowsRoot(@"C:\ProgramData\something"));
    }

    [Fact]
    public void IsUnderWindowsRoot_SiblingPrefixNotAccepted()
    {
        var windows = MinidumpLocator.ResolveWindowsDirectory();
        var parent = Path.GetDirectoryName(windows)!;
        var sibling = Path.Combine(parent, Path.GetFileName(windows) + "Evil", "Minidump");
        Assert.False(MinidumpLocator.IsUnderWindowsRoot(sibling));
    }

    [Fact]
    public void ResolveWindowsDirectory_DoesNotHonorPoisonedSystemRootEnv()
    {
        // The point of ResolveWindowsDirectory is that it reads from a Win32
        // source not backed by the process env block. Poison %SystemRoot% in
        // the current process and verify the resolver ignores it.
        var original = Environment.GetEnvironmentVariable("SystemRoot");
        try
        {
            Environment.SetEnvironmentVariable("SystemRoot", @"C:\attacker-controlled");
            var resolved = MinidumpLocator.ResolveWindowsDirectory();
            Assert.NotEqual(@"C:\attacker-controlled", resolved);
            Assert.EndsWith("Windows", resolved, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SystemRoot", original);
        }
    }
}
