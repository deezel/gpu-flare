# FLARE Architecture (60-second tour)

## Layering

```
FLARE.UI  (WPF)          - MainWindow, MainViewModel, AboutWindow
    |
    | await FlareRunner.Run(options, log, ct, deps?)
    v
FLARE.Core  (static)     - FlareRunner orchestrates collectors
    |
    +-- GpuInfo              (nvidia-smi + Vulkan SM query)
    +-- SystemInfo           (registry + GlobalMemoryStatusEx)
    +-- EventLogParser       (System.Diagnostics.Eventing.Reader, XPath;
    |                         also XML-fixture entry point ParseNvlddmkmEventXml)
    +-- MinidumpLocator      (resolves %SystemRoot%\Minidump path; copy is done by ElevatedDumpCopy)
    +-- LiveKernelDumpLocator (scans C:\Windows\LiveKernelReports for non-BSOD watchdog dumps)
    +-- ElevatedDumpCopy     (single-UAC helper: minidumps + LiveKernel dumps)
    +-- DumpAnalyzer         (PAGEDU64/MDMP/PAGE binary parse + cdb orchestration; async)
    +-- LiveKernelDumpReport (live-kernel section of the report + orphan-cache surfacing; async)
    +-- BugcheckCatalog      (bugcheck code -> name + GPU-related flag + live-dump flag)
    +-- CdbLocator/Runner    (WinDbg discovery; cdb !analyze subprocess handling)
    +-- ParallelCdbRunner    (largest-first pre-warm + size-weighted semaphore; decouples
    |                         cdb execution order from rendering order; cap min(CPU, 6))
    +-- CdbAnalysisCache     (per-dump cdb transcript cache, invalidated by size/mtime/version)
    +-- CdbDetailsSink       (collects full cdb stack traces into the paired *_dumps.md file)
    +-- CollectorHealth      (per-run notices, canaries, failure log, cap-hit truncation)
    +-- CollectionTruncation (cap-hit flags surfaced in the SCOPE block)
    +-- ReportGenerator      (ReportInput -> GeneratedReport(Main, Details?); Markdown)
    +-- ReportAnalysis       (concentration verdicts, multi-GPU localization suppression)
    +-- ReportRedaction / NvidiaDriverVersion (small report helpers)
    |
FLARE.Tests  (xUnit v3)  - pinned parsers, pipeline integration tests,
                           real-world fixture corpus (cdb / nvlddmkm / setupapi)
```

## Core principles

- **Core is pure.** No WPF references. Core is host-agnostic enough for the current WPF UI, unit tests, or a hypothetical CLI shim. All Windows-specific APIs are wrapped in a way that lets tests substitute fakes (`FlareDependencies`).
- **Collectors return data, not strings.** `ReportGenerator` is the only thing that formats for display.
- **Cancellation reaches every collector.** `CancellationToken` is threaded from the UI through `FlareRunner.Run` into each XPath event-log read and each cdb.exe invocation. cdb runs concurrently but `Parallel.ForEachAsync` short-circuits new starts on cancel and `ProcessRunner` kills in-flight cdb processes.
- **Errors are logged, not thrown.** The `Action<string>? log` callback is threaded alongside the token; collectors catch their own exceptions and surface warnings so one broken data source doesn't fail the whole report.

## Key seams

### `FlareDependencies` (`FLARE.Core/FlareRunner.cs`)

Record of delegates pointing at each collector. `FlareRunner.Run` is `async Task<FlareResult>` and takes an optional `FlareDependencies? deps` parameter - defaults to real implementations, tests pass fakes. This is how pipeline ordering, cancellation propagation, and directory creation are exercised without touching live Windows APIs. See `FlareRunnerTests` for examples.

### `ReportInput` (`FLARE.Core/ReportGenerator.cs`)

Single bundle parameter to `ReportGenerator.Generate(input, sink?)` which returns `GeneratedReport(Main, Details?)`. The optional `CdbDetailsSink` collects full cdb stack traces into the paired `*_dumps.md` file when deep analysis is enabled. Adding a new data source means: add a field to `ReportInput`, populate it in `FlareRunner.Run`, render it in `Generate` using the dynamic `section` counter. No 10-argument signature churn.

### `ReadEvents<T>` (`FLARE.Core/EventLogParser.cs`)

Helper that owns `EventLogQuery` / `EventLogReader` / per-record disposal. Each `Pull*` method provides a mapper callback (`Func<EventRecord, T?>`) and an XPath. Returning `null` from the mapper skips a record without consuming the result cap - this is how the driver-install extractors filter on vendor without writing nested `try`/`catch` blocks.

Broad filtered scans can also pass a separate scan cap. That keeps queries such as driver-install history bounded even when many Windows records are scanned before an NVIDIA match appears.

### Fixture corpus (`FLARE.Tests/Fixtures/`)

Three parsers are pinned against real-world payloads, redacted via `ReportRedaction`:

- `Cdb/` — cdb `!analyze -v` transcripts (minidumps + LiveKernel) paired with expected `ExtractCdbSummary` output.
- `EventLog/Nvlddmkm/` — `nvlddmkm` event XML payloads paired with expected `ClassifyGpuError` output (via `EventLogParser.ParseNvlddmkmEventXml`, which mirrors the live `EventLogReader` path).
- `EventLog/SetupApi/` — `setupapi.dev.log` slices paired with expected `ParseSetupApiLog` output.

`FixtureBuilder` (env-var gated, `FLARE_REBUILD_FIXTURES=1`) regenerates each corpus from the live machine; `Regenerate*ExpectedFromFixtures` rewrites the `.expected.txt` files from current parser output.

### About-box dependency list

`Directory.Build.targets` emits one `[AssemblyMetadata("Dependency.<Name>", "<Version>")]` per `PackageReference`, filtering `Microsoft.*` and `System.*` (framework-bundled). `BuildBanner.GetDependencies(asm)` walks the entry assembly and its `FLARE.*` references at runtime and feeds the AboutWindow text block — no hardcoded credits.

## Report section numbering

Sections are numbered dynamically via a local `section` counter (starts at 1, incremented as each section is emitted). Optional sections (System Identification, Driver Install History, Application Crash Correlation, etc.) don't leave numbering gaps when absent. Tests in `ReportGeneratorTests` pin both present-and-absent configurations.

## Where things live (quick lookup)

| Looking for... | File |
|---|---|
| nvlddmkm event ID to error type mapping | `EventLogParser.cs` - `ClassifyGpuError` |
| nvlddmkm fixture entry point (XML -> NvlddmkmError) | `EventLogParser.cs` - `ParseNvlddmkmEventXml` |
| NVIDIA driver version format conversion | `NvidiaDriverVersion.cs` - `ToNvidiaVersion` |
| cdb.exe auto-detection | `CdbLocator.cs` |
| cdb.exe subprocess execution and summary extraction | `CdbRunner.cs` |
| Parallel cdb scheduling (pre-warm + weighted semaphore) | `ParallelCdbRunner.cs` |
| cdb transcript cache (version + path/size/mtime key) | `CdbAnalysisCache.cs` |
| Bugcheck code -> name + GPU/live-dump flags | `BugcheckCatalog.cs` |
| Report user-path redaction | `ReportRedaction.cs` |
| UI-side redaction egress (status bar, tooltips, output path, popup) | `MainViewModel.Display` |
| UI log timestamping (`[mm:ss]`) and `Analysis complete in Xm Ys` | `MainViewModel.StampLog` / `FormatRunDuration` |
| Build-kind label in title bar and About dialog | `BuildBanner.cs` |
| About-box dependency list derivation | `BuildBanner.GetDependencies` + `Directory.Build.targets` |
| PAGEDU64 header offsets | `DumpAnalyzer.cs` - `ParseKernelDump64` (commented) |
| BAR1 / Resizable BAR detection | `GpuInfo.cs` - `ParseBar1TotalMibFromQueryOutput` |
| PCIe link state parsing | `GpuInfo.cs` - `ParsePcieFromQueryOutput` |
| Dump minidump source path | `MinidumpLocator.cs` - `GetSystemDumpDir` (reads registry) |
| LiveKernel dump source path | `LiveKernelDumpLocator.cs` (`C:\Windows\LiveKernelReports`) |
| Correlation window and logic | `EventLogParser.cs` - `CorrelateWithAppCrashes` |
| MinVer-derived version | `FLARE.UI.csproj` (`MinVerTagPrefix=v`); `FlareIsRelease` target |

## Local storage layout

```
%LOCALAPPDATA%\FLARE\
├── DO_NOT_SHARE\
│   ├── Minidumps\          - kernel minidumps copied from %SystemRoot%\Minidump
│   ├── LiveKernelDumps\    - watchdog dumps copied from C:\Windows\LiveKernelReports,
│   │   ├── WATCHDOG\         per-category subdirs preserved (WATCHDOG / WATCHDOG4400 / WATCHDOG4401 / ...)
│   │   └── ...\
│   ├── CdbCache\           - raw cdb !analyze -v transcripts (per-dump, keyed by size+mtime)
│   └── Staging\            - transient holding area for the elevated dump-copy helper
├── Reports\                - report output dir (fixed location); holds only the generated .md files
├── settings.json
└── fatal.log[.old]
```

The report folder is fixed at `%LOCALAPPDATA%\FLARE\Reports\` and contains only
the generated `.md` files. Anything sensitive (raw kernel memory, unscrubbed
stack traces) accumulates under the `DO_NOT_SHARE` root — the naming is the
point: a folder the user has to actively ignore the warning in to share.
`FlareStorage` is the single source of truth for these paths; `FlareRunner`,
`CdbAnalysisCache`, `LiveKernelDumpReport`, and the test `FixtureBuilder` all
resolve through it.

## Non-goals

- No real-time monitoring / tray / daemon - FLARE is a "run it, read the report" tool.
- No general-purpose event log viewer. Focus stays on NVIDIA GPU correlation.
- No cross-platform support. Windows-only by design (nvlddmkm, Win32 minidump format, WMI/registry APIs).
