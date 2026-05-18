# FLARE - Fault Log Analysis & Reboot Examination

![Build](../../actions/workflows/build.yml/badge.svg)

A Windows tool for NVIDIA GPU diagnostics. Collects data from Windows Event Log (`nvlddmkm` driver errors), kernel crash dumps, and nvidia-smi, then correlates it into a comprehensive diagnostic report.

![FLARE screenshot](screenshots/FLARE.png)

## What it does

FLARE gathers and correlates data from multiple sources:

1. **GPU identification** via nvidia-smi (name, driver, UUID) and Vulkan (SM count query using `VkPhysicalDeviceShaderSMBuiltinsPropertiesNV` — the only reliable method on consumer GPUs). Includes current vs max PCIe link gen/width at sample time, with an idle-power caveat when the sampled link is below its capability.
2. **System identification** — motherboard make/model, BIOS vendor/version/date, CPU, and total RAM, read from the registry's SMBIOS snapshot + `GlobalMemoryStatusEx`. Useful context for troubleshooting upstream causes (outdated BIOS with known NVIDIA quirks, misnegotiated PCIe link), not a conclusion on its own.
3. **NVIDIA driver errors** from Windows Event Log (`nvlddmkm` events):
   - Event ID 13 — SM errors with GPC/TPC/SM coordinates and error types (Illegal Instruction Encoding, Misaligned PC, etc.)
   - Event ID 14 — PCIe/ECC errors (SRAM uncorrectable, command re-execution)
   - Event ID 153 — TDR (Timeout Detection and Recovery)
4. **System crash events** — BSODs (WER ID 1001) and unexpected reboots (Kernel-Power ID 41)
5. **Application crash correlation** — pulls application crashes (Event ID 1000) and hangs (Event ID 1002) from the Application log, then correlates them by timestamp with GPU errors to identify which applications were affected
6. **Kernel minidump analysis** — parses PAGEDU64/MDMP crash dumps to extract bugcheck codes and parameters, identifies GPU-related crashes (VIDEO_TDR_FAILURE, VIDEO_TDR_TIMEOUT_DETECTED, VIDEO_SCHEDULER_INTERNAL_ERROR)
7. **Live kernel watchdog dumps** — scans `C:\Windows\LiveKernelReports` for non-BSOD kernel dumps (typically `WATCHDOG`, `WATCHDOG4400`, `WATCHDOG4401` subdirectories). These capture GPU engine timeouts (`0x141 VIDEO_ENGINE_TIMEOUT_DETECTED`), display-driver live dumps (`0x1B0`, `0x1B8`), TDR timeouts (`0x117`), and similar live-dump bugcheck codes that occur when Windows attempts driver recovery without a full BSOD — often the only diagnostic artifact when an application hang has no matching `nvlddmkm` event or WER crash.
8. **Driver install history** — reads Kernel-PnP, DeviceSetupManager, and `setupapi.dev.log` to reconstruct the driver install timeline, annotated onto the error frequency chart so you can see whether a driver change coincided with an error surge
9. **Report generation** — Markdown reports with error summary, SM coordinate concentration analysis, weekly error frequency chart with driver annotations, full error timeline, application crash correlation, crash events, and (when crash dump analysis is enabled) crash dump + live kernel dump sections. Full cdb stack traces are routed to a companion `_dumps.md` file with collapsible per-dump blocks so the main report stays compact for forum-pasting.

## Requirements

- Windows 10/11
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (or SDK for building from source)
- NVIDIA GPU with drivers installed (for nvidia-smi)
- No elevation needed up front. FLARE runs unelevated and prompts for UAC only when crash dump analysis is enabled and system minidumps need to be copied; that brief helper step is the only code that runs as administrator. All other collection and cdb analysis runs unelevated.

## Download

Grab the latest build from the [GitHub Releases page](../../releases). Single `FLARE.exe`, no installer. Requires the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0). Per-commit CI builds are also available from [GitHub Actions artifacts](../../actions), but those expire after 90 days — use Releases for anything you want to keep.

The title bar tells you which kind of build you're running: a clean `FLARE <version> - …` is a tagged release; `[SNAPSHOT] FLARE <version>-alpha.0.<N>+<hash> - …` is a per-commit CI artifact (the `-alpha.0.<N>` suffix is MinVer's commit height past the most recent `v*` tag); `[DEV BUILD] FLARE <version>-alpha.0.<N>+dev - …` is a local `dotnet build`. The About dialog repeats this information.

**Unsigned binary — and it's going to stay that way.** FLARE releases are not code-signed and will not be. Windows SmartScreen may warn on first run. This is a permanent, deliberate project decision; please don't file issues or review comments asking for it to change.

Why it's settled:

- FLARE is a single-maintainer, zero-revenue diagnostic utility. Authenticode code-signing certificates (especially EV, which is what actually bypasses SmartScreen reputation gating) are an ongoing paid subscription plus HSM/token logistics. That cost-and-ops overhead is not proportionate to a hobby tool.
- A signature on the binary proves only that someone bought a cert under some name. It does not prove the code does what the README says, and it does not prove the build wasn't tampered with upstream. The trust problem a signature *appears* to solve is not actually the trust problem users have here.
- FLARE's actual provenance story is different and, for this project's threat model, stronger: every line of source is in this repo, every release is built by GitHub Actions from a tagged commit using SHA-pinned workflow actions, and the release artifact is accompanied by a `SHA256SUMS.txt`. If you want to verify a binary, rebuild it — the CI workflow is a dozen steps you can run locally. If you don't trust a cert-issuance authority but *do* trust reading source, that path is open to you and it is not open with most signed software.
- If the project's ownership, funding model, or distribution story ever changes in a way that makes signing proportionate, this note will be updated alongside that change. Until then, "please sign the binary" is not an actionable request.

If the above doesn't meet your personal trust bar, the supported answers are: (1) build from source using the `dotnet build` instructions below; (2) don't run FLARE. Both are fine.

For users who do choose to run the release binary:

- Download FLARE only from the [official GitHub Releases page](../../releases) — not from mirrors, third-party sites, or file attachments elsewhere.
- When a `SHA256SUMS.txt` file is published alongside the zip, verify the zip's hash before running:
  ```
  Get-FileHash -Algorithm SHA256 FLARE-<version>-win-x64.zip
  ```
  and compare against the value in `SHA256SUMS.txt`.

## Building from source

```
dotnet build FLARE.slnx
dotnet test FLARE.slnx
```

## Project structure

```
FLARE/
├── FLARE.Core/          # Shared logic (GPU info, event log parsing, dump analysis, report generation)
├── FLARE.UI/            # WPF desktop application
├── FLARE.Tests/         # xUnit tests
├── FLARE.slnx
└── .github/workflows/   # CI build pipeline
```

## Crash dump analysis with cdb.exe

FLARE can optionally use **cdb.exe** for detailed crash dump analysis. cdb is the command-line debugger from the Windows Debugger (WinDbg) package — same analysis engine as the full GUI, but scriptable. FLARE runs `!analyze -v` on each dump to extract:

- Faulting module and driver name
- Stack traces showing the crash path
- Bugcheck classification strings
- Process context at the time of crash

The crash dump analysis checkbox copies both system minidumps and live kernel watchdog dumps from `C:\Windows\LiveKernelReports`, then runs FLARE's built-in dump parser on each. If cdb.exe is detected, FLARE also runs `!analyze -v` on every dump from either source; when the checkbox is off, FLARE does not copy or analyze any dumps. Both sources are read in a single elevated round-trip — one UAC prompt covers both. FLARE itself stays unelevated; only the crash-dump copy helper prompts for elevation when needed. cdb.exe is run by the unelevated parent process when available.

FLARE auto-detects cdb.exe under Microsoft debugger locations — Windows Kits (`Program Files\Windows Kits\10\Debuggers`), WinDbg app packages (`Program Files\WindowsApps\*WinDbg*`), and per-user WinDbg aliases under `LocalAppData\Microsoft\{WindowsApps,WinDbg}`. PATH is intentionally not used, and there is no configured cdb path override.

**Note:** cdb deep analysis takes up to 30 seconds per dump file when running cold. cdb runs the largest dump first (to warm the symbol cache) and then the remainder concurrently up to `min(CPU count, 6)` with size-weighted scheduling, typically landing real-world batches in a third of the serial time. Results are cached under `%LOCALAPPDATA%\FLARE\DO_NOT_SHARE\CdbCache`, keyed on the dump's path, size, and mtime, so subsequent runs reuse the prior transcript and only newly-copied dumps incur the cost. Delete that cache folder to force re-analysis. The analysis can be cancelled at any time via the Cancel button.

### Installing cdb.exe

```
winget install Microsoft.WinDbg
```

Alternatively, install the [Windows SDK](https://developer.microsoft.com/en-us/windows/downloads/windows-sdk/) and select "Debugging Tools for Windows" during setup.

## Output

Reports are saved to `%LOCALAPPDATA%\FLARE\Reports\` as paired Markdown files: a main `flare_report_<ts>.md` (the report itself — scoped for forum-pasting, PR comments, GitHub issues) and, when crash dump analysis is enabled and cdb is available, a companion `flare_report_<ts>_dumps.md` carrying full stack traces, one fenced block per dump under a `### filename.dmp` heading. The main report keeps the structured fields per dump (`MODULE_NAME`, `FAILURE_BUCKET_ID`, etc.) and links to the corresponding stack-trace block in the dumps file. The **Open** button in the UI jumps to the folder and highlights the main file. The report folder contains only the generated `.md` files — no dumps, no caches — so it's safe to zip, share, or sync as-is. If you want a specific report somewhere else, copy the `.md` files after the run.

Report output bytes are identical regardless of cdb scheduling (serial or parallel) given the same input set.

A sample run is checked in under [examples/](examples/) — [`flare_report_20260516_104327.md`](examples/flare_report_20260516_104327.md) and its companion [`flare_report_20260516_104327_dumps.md`](examples/flare_report_20260516_104327_dumps.md) — covering a window with several real TDR storms, so you can see the shape FLARE produces under genuine fault load before running it yourself.

The in-document anchor links (the Contents TOC, the per-dump cross-references between the main report and the `_dumps.md` companion) are aimed at local Markdown viewers and editor previews, which follow the usual CommonMark slugger. If you paste a report into a GitHub PR, issue, or Gist, those internal anchors may not navigate cleanly — GitHub's anchor scheme is stricter and not what FLARE targets. The prose itself is the share surface; treat the in-document links as a local-reading aid.

Upgrading from 0.6.x? On first launch, FLARE auto-migrates `%LOCALAPPDATA%\FLARE\CdbCache\` and the default `%LOCALAPPDATA%\FLARE\Reports\minidumps\` into the new `DO_NOT_SHARE\` layout. If you had pointed 0.6.x at a custom report folder, your old `<custom>\minidumps\` stays there — move its contents into `%LOCALAPPDATA%\FLARE\DO_NOT_SHARE\Minidumps\` yourself (or delete it).

When crash dump analysis is enabled, system minidumps are copied from the configured location (`HKLM\SYSTEM\CurrentControlSet\Control\CrashControl\MinidumpDir`, or `%SystemRoot%\Minidump` if unset) into `%LOCALAPPDATA%\FLARE\DO_NOT_SHARE\Minidumps\`, and live kernel watchdog dumps are copied from `C:\Windows\LiveKernelReports` into `%LOCALAPPDATA%\FLARE\DO_NOT_SHARE\LiveKernelDumps\<category>\` (preserving the source subdirectory names — `WATCHDOG`, `WATCHDOG4400`, `WATCHDOG4401`, etc.). Both live outside the report folder. The cdb `!analyze -v` transcript cache lives alongside it under `%LOCALAPPDATA%\FLARE\DO_NOT_SHARE\CdbCache\`. The folder is named for what it is: raw kernel-memory fragments and stack traces with local paths that aren't meant to travel with the report.

Each run only copies dumps that fall inside the current Max Days window; older dumps copied by a previous, wider-window run stay where they landed and aren't auto-deleted (so widening the window later doesn't lose dumps Windows has since cleared from its own folders). The cache can grow over time — especially with large LiveKernel watchdog dumps. To clean it up, use the **Open** button in the UI (it jumps to the reports folder), step up into `%LOCALAPPDATA%\FLARE\DO_NOT_SHARE\`, and delete what you don't need.

When a LiveKernel `.dmp` is deleted but its cdb `!analyze -v` transcript is still in `%LOCALAPPDATA%\FLARE\DO_NOT_SHARE\CdbCache\`, the preserved analysis surfaces in the report under a separate **Cached analyses (source dump no longer present)** subsection within the live-kernel section, labeled `(source removed)`. Delete the matching `.cdb.txt` file under `CdbCache\` to drop the entry from future reports.

### Redaction

Redirected user folders are handled through the actual environment roots too: paths rooted at `%APPDATA%`, `%LOCALAPPDATA%`, `%TEMP%`, `%TMP%`, or OneDrive environment variables are rewritten to those markers instead of leaking the concrete directory.

**Redact identifiers** is on by default, so the saved `.md` report can be pasted into a forum thread or support ticket without first scrubbing it by hand. Redaction replaces the GPU UUID and the computer name with `[redacted]` and rewrites Windows user-profile paths to `%USERPROFILE%` in cdb stack traces. Process names, driver/module names, and stack frames are always preserved — without them the report has no diagnostic value.

When redaction is enabled, every user-visible string FLARE writes goes through the same scrubbing — the visible run log, the bottom status bar, the OUTPUT path row and tooltip, the cdb status tooltip, and the unhandled-exception popup. Toggling redaction live re-renders the persistent surfaces (OUTPUT path, cdb tooltip); status-bar messages already shown stay as written, the next status update reflects the new setting. Raw cdb transcripts cached under `DO_NOT_SHARE\CdbCache\` are not redacted; they stay local and redaction runs when report text is rendered.

Uncheck the option only when a troubleshooting conversation needs the GPU UUID in the report text itself.

## How it works (technical details)

- Event log data is read via the `System.Diagnostics.Eventing.Reader` API with XPath filters, because the `nvlddmkm` event Message field is always empty on these events — the actual error data lives in the `EventRecord.Properties` collection (`EventData/Data` XML nodes)
- nvidia-smi cannot report SM count on consumer GPUs — the Vulkan `VK_NV_shader_sm_builtins` extension is queried instead, iterating physical devices to skip integrated GPUs
- Consumer GPUs report serial number as "0", so UUID is used for identification
- Crash dumps carry one of three signatures: PAGEDU64/PAGEDUMP (kernel-mode), MDMP (user-mode), or PAGE (32-bit kernel). Kernel-mode 64-bit dumps carry the bugcheck code at offset 0x38

## Disclaimer

This tool is provided as-is with no warranty. It reads system logs and crash dumps — it does not modify any system files, drivers, or settings.

## License

[GPL v3](LICENSE)
