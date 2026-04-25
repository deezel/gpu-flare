using System.Collections.Generic;

namespace FLARE.Core;

public static class BugcheckCatalog
{
    private sealed record Entry(string Name, string? Extra, bool Gpu);

    // IsKnown doubles as DumpAnalyzer's "parser landed on a sane offset" signal —
    // unknown-but-valid codes trip the heuristic-scan fallback, so the catalog
    // covers common non-GPU codes too.
    private static readonly Dictionary<uint, Entry> Entries = new()
    {
        { 0x0A,  new("IRQL_NOT_LESS_OR_EQUAL",                  null,                     false) },
        { 0x1A,  new("MEMORY_MANAGEMENT",                       null,                     false) },
        { 0x3B,  new("SYSTEM_SERVICE_EXCEPTION",                null,                     false) },
        { 0x50,  new("PAGE_FAULT_IN_NONPAGED_AREA",             null,                     false) },
        { 0x7E,  new("SYSTEM_THREAD_EXCEPTION_NOT_HANDLED",     null,                     false) },
        { 0x7F,  new("UNEXPECTED_KERNEL_MODE_TRAP",             null,                     false) },
        { 0x9F,  new("DRIVER_POWER_STATE_FAILURE",              null,                     false) },
        { 0xC1,  new("SPECIAL_POOL_DETECTED_MEMORY_CORRUPTION", null,                     false) },
        { 0xC5,  new("DRIVER_CORRUPTED_EXPOOL",                 null,                     false) },
        { 0xD1,  new("DRIVER_IRQL_NOT_LESS_OR_EQUAL",           null,                     false) },
        { 0xE2,  new("MANUALLY_INITIATED_CRASH",                null,                     false) },
        { 0xEA,  new("THREAD_STUCK_IN_DEVICE_DRIVER",           "often GPU driver",       true)  },
        { 0xEF,  new("CRITICAL_PROCESS_DIED",                   null,                     false) },
        { 0xF7,  new("DRIVER_OVERRAN_STACK_BUFFER",             null,                     false) },
        { 0x10D, new("WDF_VIOLATION",                           null,                     false) },
        { 0x116, new("VIDEO_TDR_FAILURE",                       "GPU stopped responding", true)  },
        { 0x117, new("VIDEO_TDR_TIMEOUT_DETECTED",              null,                     true)  },
        { 0x119, new("VIDEO_SCHEDULER_INTERNAL_ERROR",          null,                     true)  },
        { 0x124, new("WHEA_UNCORRECTABLE_ERROR",                null,                     false) },
        { 0x133, new("DPC_WATCHDOG_VIOLATION",                  null,                     false) },
        { 0x278, new("KERNEL_MODE_HEAP_CORRUPTION",             null,                     false) },
        { 0x307, new("KERNEL_STORAGE_SLOT_IN_USE",              null,                     false) },
    };

    public static bool IsGpuRelated(uint code) =>
        Entries.TryGetValue(code, out var e) && e.Gpu;

    public static bool IsKnown(uint code) => Entries.ContainsKey(code);

    public static string GetName(uint code) =>
        Entries.TryGetValue(code, out var e) ? e.Name : $"0x{code:X8}";

    public static string GetKernelPowerDescription(uint code)
    {
        // code 0 = Kernel-Power hard-reboot sentinel
        if (code == 0) return "Unexpected power loss / hard reboot";
        if (Entries.TryGetValue(code, out var e))
            return e.Extra is null ? e.Name : $"{e.Name} ({e.Extra})";
        return $"Bugcheck 0x{code:X8}";
    }
}
