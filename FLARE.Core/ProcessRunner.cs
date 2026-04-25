using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace FLARE.Core;

public static class ProcessRunner
{
    // Per-child-process budget: applied once per nvidia-smi invocation and once
    // per cdb !analyze -v (DumpAnalyzer spawns cdb separately per dump). On
    // fast hardware a full batch runs in ~15s total; the 30s per-dump ceiling
    // leaves slower machines plenty of headroom for normal kernel minidumps.
    // A single dump hitting the ceiling would need to be pathologically
    // large. Deliberate, measured on real hardware; not a guess. Do not raise
    // on the theory that cdb "might" need longer.
    internal static readonly TimeSpan DefaultSyncTimeout = TimeSpan.FromSeconds(30);

    public static string Run(string exe, params string[] args) =>
        RunInternal(exe, null, CancellationToken.None, args);

    public static string RunWithLog(string exe, Action<string>? log, params string[] args) =>
        RunInternal(exe, log, CancellationToken.None, args);

    public static string RunWithLog(string exe, Action<string>? log, CancellationToken ct, params string[] args) =>
        RunInternal(exe, log, ct, args);

    static string RunInternal(string exe, Action<string>? log, CancellationToken ct, string[] args)
    {
        using var timeoutCts = new CancellationTokenSource(DefaultSyncTimeout);
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
                log?.Invoke($"Warning: {exe} timed out after {DefaultSyncTimeout.TotalSeconds:F0}s and was killed.");
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
