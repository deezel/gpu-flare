using FLARE.Core;

namespace FLARE.Tests;

public class ProcessRunnerTests
{
    [Fact]
    public void Run_SimpleCommand_ReturnsOutput()
    {
        var result = ProcessRunner.Run("cmd", "/c", "echo hello");
        Assert.Contains("hello", result);
    }

    [Fact]
    public void Run_NonexistentCommand_ReturnsEmpty()
    {
        var result = ProcessRunner.Run("definitely_not_a_real_command_12345");
        Assert.Equal("", result);
    }

    [Fact]
    public void Run_StderrOutput_DoesNotDeadlock()
    {
        var result = ProcessRunner.Run("cmd", "/c", "echo stdout_text && echo stderr_text 1>&2");
        Assert.Contains("stdout_text", result);
    }

    [Fact]
    public void RunWithLog_NonzeroExitWithStderr_RoutesStderrToLog()
    {
        // Pipeline invariant: when a collector shells out to nvidia-smi/cdb and the
        // child exits non-zero with stderr output, the user must see that stderr in
        // the log pane. Without this, silent shell-out failures surface as a blank
        // report section with no explanation.
        var warnings = new List<string>();
        var result = ProcessRunner.RunWithLog("cmd", warnings.Add,
            "/c", "echo error_detail 1>&2 && exit 1");

        Assert.Equal("", result);
        Assert.Contains(warnings, w => w.Contains("exit 1") && w.Contains("error_detail"));
    }

    [Fact]
    public void RunWithLog_NonexistentExe_LogsNotFoundWarning()
    {
        var warnings = new List<string>();
        var result = ProcessRunner.RunWithLog("definitely_not_a_real_command_98765", warnings.Add);

        Assert.Equal("", result);
        Assert.NotEmpty(warnings);
    }

    [Fact]
    public void RunWithLog_CancelledToken_ThrowsInsteadOfHanging()
    {
        // Pipeline invariant: when the UI fires Cancel mid-collection, a slow
        // nvidia-smi invocation must not keep the background task alive. This test
        // uses a long ping to simulate a slow child; cancellation routes to
        // Process.Kill via the internal registration and throws OCE to the caller.
        var warnings = new List<string>();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.Throws<OperationCanceledException>(() =>
            ProcessRunner.RunWithLog("ping", warnings.Add, cts.Token, "-n", "30", "127.0.0.1"));
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5),
            $"Cancellation did not interrupt the child in time (elapsed {sw.Elapsed.TotalSeconds:F1}s)");
    }
}
