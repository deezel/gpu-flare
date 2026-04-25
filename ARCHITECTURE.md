# FLARE Architecture (60-second tour)

## Layering

```
FLARE.UI  (WPF)          - MainWindow, MainViewModel, AboutWindow
    |
    | FlareRunner.Run(options, log, ct, deps?)
    v
FLARE.Core  (static)     - FlareRunner orchestrates collectors
    |
    +-- GpuInfo          (nvidia-smi + Vulkan SM query)
    +-- SystemInfo       (registry + GlobalMemoryStatusEx)
    +-- EventLogParser   (System.Diagnostics.Eventing.Reader, XPath)
    +-- MinidumpLocator  (resolves %SystemRoot%\Minidump path; copy is done by ElevatedDumpCopy)
    +-- DumpAnalyzer     (PAGEDU64/MDMP binary parse + optional cdb orchestration)
    +-- CdbLocator/Runner (WinDbg discovery + cdb !analyze subprocess handling)
    +-- CdbAnalysisCache (per-user cdb transcript cache, invalidated by size/mtime/version)
    +-- ReportGenerator  (ReportInput -> plain-text report)
    +-- ReportRedaction / NvidiaDriverVersion (small report helpers)
    |
FLARE.Tests  (xUnit)     - pinned parsers, pipeline integration tests
```

## Core principles

- **Core is pure.** No WPF references. Core is host-agnostic enough for the current WPF UI, unit tests, or a hypothetical CLI shim. All Windows-specific APIs are wrapped in a way that lets tests substitute fakes (`FlareDependencies`).
- **Collectors return data, not strings.** `ReportGenerator` is the only thing that formats for display.
- **Cancellation reaches every collector.** `CancellationToken` is threaded from the UI through `FlareRunner.Run` into each XPath event-log read and each cdb.exe invocation.
- **Errors are logged, not thrown.** The `Action<string>? log` callback is threaded alongside the token; collectors catch their own exceptions and surface warnings so one broken data source doesn't fail the whole report.

## Key seams

### `FlareDependencies` (`FLARE.Core/FlareRunner.cs`)

Record of delegates pointing at each collector. `FlareRunner.Run` takes an optional `FlareDependencies? deps` parameter - defaults to real implementations, tests pass fakes. This is how pipeline ordering, cancellation propagation, and directory creation are exercised without touching live Windows APIs. See `FlareRunnerTests` for examples.

### `ReportInput` (`FLARE.Core/ReportGenerator.cs`)

Single bundle parameter to `ReportGenerator.Generate(input)`. Adding a new data source means: add a field to `ReportInput`, populate it in `FlareRunner.Run`, render it in `Generate` using the dynamic `section` counter. No 10-argument signature churn.

### `ReadEvents<T>` (`FLARE.Core/EventLogParser.cs`)

Helper that owns `EventLogQuery` / `EventLogReader` / per-record disposal. Each `Pull*` method provides a mapper callback (`Func<EventRecord, T?>`) and an XPath. Returning `null` from the mapper skips a record without consuming the result cap - this is how the driver-install extractors filter on vendor without writing nested `try`/`catch` blocks.

Broad filtered scans can also pass a separate scan cap. That keeps queries such as driver-install history bounded even when many Windows records are scanned before an NVIDIA match appears.

## Report section numbering

Sections are numbered dynamically via a local `section` counter (starts at 1, incremented as each section is emitted). Optional sections (System Identification, Driver Install History, Application Crash Correlation, etc.) don't leave numbering gaps when absent. Tests in `ReportGeneratorTests` pin both present-and-absent configurations.

## Where things live (quick lookup)

| Looking for... | File |
|---|---|
| nvlddmkm event ID to error type mapping | `EventLogParser.cs` - `ClassifyGpuError` |
| NVIDIA driver version format conversion | `NvidiaDriverVersion.cs` - `ToNvidiaVersion` |
| cdb.exe auto-detection | `CdbLocator.cs` |
| cdb.exe subprocess execution and summary extraction | `CdbRunner.cs` |
| cdb transcript cache (version + path/size/mtime key) | `CdbAnalysisCache.cs` |
| report user-path redaction | `ReportRedaction.cs` |
| UI-side redaction egress (status bar, tooltips, output path, popup) | `MainViewModel.Display` |
| build-kind label in title bar and About dialog | `BuildBanner.cs` |
| PAGEDU64 header offsets | `DumpAnalyzer.cs` - `ParseKernelDump64` (commented) |
| BAR1 / Resizable BAR detection | `GpuInfo.cs` - `ParseBar1TotalMibFromQueryOutput` |
| PCIe link state parsing | `GpuInfo.cs` - `ParsePcieFromQueryOutput` |
| Dump minidump source path | `MinidumpLocator.cs` - `GetSystemDumpDir` (reads registry) |
| Correlation window and logic | `EventLogParser.cs` - `CorrelateWithAppCrashes` |

## Local storage layout

```
%LOCALAPPDATA%\FLARE\
├── DO_NOT_SHARE\
│   ├── Minidumps\      - kernel dumps copied from %SystemRoot%\Minidump
│   ├── CdbCache\       - raw cdb !analyze -v transcripts (per-dump, keyed by size+mtime)
│   └── Staging\        - transient holding area for the elevated dump-copy helper
├── Reports\            - report output dir (fixed location); holds only the generated .txt
├── settings.json
└── fatal.log[.old]
```

The user-configurable report folder contains only the `.txt`. Anything sensitive
(raw kernel memory, unscrubbed stack traces) accumulates under the `DO_NOT_SHARE`
root — the naming is the point: a folder the user has to actively ignore the
warning in to share. `FlareStorage` is the single source of truth for these
paths; both `FlareRunner` and `CdbAnalysisCache` resolve through it.

## Non-goals

- No real-time monitoring / tray / daemon - FLARE is a "run it, read the report" tool.
- No general-purpose event log viewer. Focus stays on NVIDIA GPU correlation.
- No cross-platform support. Windows-only by design (nvlddmkm, Win32 minidump format, WMI/registry APIs).
