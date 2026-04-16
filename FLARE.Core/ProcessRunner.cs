using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace FLARE.Core;

public static class ProcessRunner
{
    public static string Run(string exe, params string[] args) => RunInternal(exe, null, args);

    public static string RunWithLog(string exe, Action<string>? log, params string[] args) => RunInternal(exe, log, args);

    static string RunInternal(string exe, Action<string>? log, string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = string.Join(" ", args),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null)
            {
                log?.Invoke($"Warning: {exe} could not be started (not found?).");
                return "";
            }

            // Read stderr on a separate thread to prevent deadlock when both
            // stdout and stderr buffers fill simultaneously.
            string stderr = "";
            var stderrTask = Task.Run(() => stderr = proc.StandardError.ReadToEnd());
            string output = proc.StandardOutput.ReadToEnd();
            stderrTask.Wait();

            proc.WaitForExit(30000);
            if (proc.ExitCode != 0 && !string.IsNullOrWhiteSpace(stderr))
                log?.Invoke($"Warning: {exe} exit {proc.ExitCode}: {stderr.Trim()}");
            return output.Trim();
        }
        catch (Exception ex)
        {
            log?.Invoke($"Warning: {exe} failed: {ex.Message}");
            return "";
        }
    }

    public static async Task<string> RunAsync(string exe, CancellationToken ct, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = string.Join(" ", args),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start {exe}");

        // Kill the process when cancellation is requested
        await using var reg = ct.Register(() =>
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
        });

        var stderrTask = proc.StandardError.ReadToEndAsync(ct);
        var output = await proc.StandardOutput.ReadToEndAsync(ct);
        await stderrTask;

        await proc.WaitForExitAsync(ct);
        // The kill registration can race: streams close cleanly and WaitForExitAsync
        // returns immediately if the process is already dead, so neither throws OCE
        // even when the token was cancelled. Check explicitly.
        ct.ThrowIfCancellationRequested();
        return output.Trim();
    }
}
