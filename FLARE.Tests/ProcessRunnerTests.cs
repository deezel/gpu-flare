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
    public async Task RunAsync_CanBeCancelled()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await ProcessRunner.RunAsync("ping", cts.Token, "-n", "100", "127.0.0.1");
        });
    }

    [Fact]
    public async Task RunAsync_ReturnsOutput()
    {
        var result = await ProcessRunner.RunAsync("cmd", CancellationToken.None, "/c", "echo async_hello");
        Assert.Contains("async_hello", result);
    }
}
