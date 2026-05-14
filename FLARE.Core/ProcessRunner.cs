using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace FLARE.Core;

public static class ProcessRunner
{
    internal static readonly TimeSpan DefaultSyncTimeout = TimeSpan.FromSeconds(30);

    // LiveKernel watchdog dumps are 100x a minidump's size and cdb's first run also
    // downloads symbols from the public MS server; 30s is too tight for that path.
    internal static readonly TimeSpan CdbSyncTimeout = TimeSpan.FromSeconds(120);

    public static string Run(string exe, params string[] args) =>
        RunInternal(exe, null, CancellationToken.None, DefaultSyncTimeout, args);

    public static string RunWithLog(string exe, Action<string>? log, params string[] args) =>
        RunInternal(exe, log, CancellationToken.None, DefaultSyncTimeout, args);

    public static string RunWithLog(string exe, Action<string>? log, CancellationToken ct, params string[] args) =>
        RunInternal(exe, log, ct, DefaultSyncTimeout, args);

    public static string RunWithLog(string exe, Action<string>? log, CancellationToken ct, TimeSpan timeout, params string[] args) =>
        RunInternal(exe, log, ct, timeout, args);

    static string RunInternal(string exe, Action<string>? log, CancellationToken ct, TimeSpan timeout, string[] args)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var proc = Process.Start(psi);
            if (proc == null)
            {
                log?.Invoke($"Warning: {exe} could not be started (not found?).");
                return "";
            }

            // Register kill-on-cancel BEFORE the blocking reads so Cancel interrupts ReadToEnd.
            using var reg = linked.Token.Register(() =>
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
            });

            string stderr = "";
            var stderrTask = Task.Run(() => stderr = proc.StandardError.ReadToEnd());
            string output = proc.StandardOutput.ReadToEnd();
            stderrTask.Wait();
            // Bounded: if Kill-on-cancel silently failed after pipes drained, hanging here
            // would freeze the UI Cancel button.
            if (!proc.WaitForExit(5000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                log?.Invoke($"Warning: {exe} did not exit within 5s after stdout closed; killed.");
            }

            // User cancel must propagate; watchdog timeout logs and returns empty.
            if (ct.IsCancellationRequested)
                ct.ThrowIfCancellationRequested();
            if (timeoutCts.IsCancellationRequested)
            {
                log?.Invoke($"Warning: {exe} timed out after {timeout.TotalSeconds:F0}s and was killed.");
                return "";
            }

            if (proc.ExitCode != 0 && !string.IsNullOrWhiteSpace(stderr))
                log?.Invoke($"Warning: {exe} exit {proc.ExitCode}: {stderr.Trim()}");
            return output.Trim();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log?.Invoke($"Warning: {exe} failed: {ex.Message}");
            return "";
        }
    }

}
