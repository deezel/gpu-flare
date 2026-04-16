# FLARE - Fault Log Analysis & Reboot Examination

![Build](../../actions/workflows/build.yml/badge.svg)

A Windows tool for NVIDIA GPU diagnostics. Collects data from Windows Event Log (`nvlddmkm` driver errors), kernel crash dumps, and nvidia-smi, then correlates it into a comprehensive diagnostic report.

![FLARE screenshot](screenshots/FLARE.png)

## What it does

FLARE gathers and correlates data from multiple sources:

1. **GPU identification** via nvidia-smi (name, driver, UUID) and Vulkan (SM count query using `VkPhysicalDeviceShaderSMBuiltinsPropertiesNV` — the only reliable method on consumer GPUs)
2. **NVIDIA driver errors** from Windows Event Log (`nvlddmkm` events):
   - Event ID 13 — SM errors with GPC/TPC/SM coordinates and error types (Illegal Instruction Encoding, Misaligned PC, etc.)
   - Event ID 14 — PCIe/ECC errors (SRAM uncorrectable, command re-execution)
   - Event ID 153 — TDR (Timeout Detection and Recovery)
3. **System crash events** — BSODs (WER ID 1001) and unexpected reboots (Kernel-Power ID 41)
4. **Application crash correlation** — pulls application crashes (Event ID 1000) and hangs (Event ID 1002) from the Application log, then correlates them by timestamp with GPU errors to identify which applications were affected
5. **Kernel minidump analysis** — parses PAGEDU64/MDMP crash dumps to extract bugcheck codes and parameters, identifies GPU-related crashes (VIDEO_TDR_FAILURE, VIDEO_TDR_TIMEOUT_DETECTED, VIDEO_SCHEDULER_INTERNAL_ERROR)
6. **Driver install history** — reads Kernel-PnP, DeviceSetupManager, and `setupapi.dev.log` to reconstruct the driver install timeline, annotated onto the error frequency chart so you can see whether a driver change coincided with an error surge
7. **Report generation** — error summary with SM coordinate analysis, probability calculations, weekly error frequency chart with driver annotations, full error timeline, application crash correlation, crash events

## Requirements

- Windows 10/11
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (or SDK for building from source)
- NVIDIA GPU with drivers installed (for nvidia-smi)
- Administrator elevation recommended (for accessing minidumps and enabling deep analysis)

## Download

Grab the latest build from [GitHub Actions artifacts](../../actions). Single `FLARE.exe`, no installer. Requires the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).

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

## Deep analysis with cdb.exe

FLARE can optionally use **cdb.exe** for detailed crash dump analysis. cdb is the command-line debugger from the Windows Debugger (WinDbg) package — same analysis engine as the full GUI, but scriptable. FLARE runs `!analyze -v` on each dump to extract:

- Faulting module and driver name
- Stack traces showing the crash path
- Bugcheck classification strings
- Process context at the time of crash

The deep analysis checkbox in the UI requires both administrator elevation and cdb.exe to be present on the system. FLARE checks PATH, Windows SDK install locations, and WinDbg app packages automatically.

**Note:** cdb deep analysis takes up to 30 seconds per dump file. With many dumps in `C:\WINDOWS\Minidump`, the total can run to several minutes. The analysis can be cancelled at any time via the Cancel button.

### Installing cdb.exe

```
winget install Microsoft.WinDbg
```

Alternatively, install the [Windows SDK](https://developer.microsoft.com/en-us/windows/downloads/windows-sdk/) and select "Debugging Tools for Windows" during setup.

## Output

Reports are saved to the configured output directory (defaults to `reports/` in the current working directory). Crash dumps are copied from the system minidump directory (`HKLM\SYSTEM\CurrentControlSet\Control\CrashControl\MinidumpDir`, or `%SystemRoot%\Minidump` if unset) to a `minidumps/` subdirectory within the report folder.

## How it works (technical details)

- Event log data is read via the `System.Diagnostics.Eventing.Reader` API with XPath filters, because the `nvlddmkm` event Message field is always empty on these events — the actual error data lives in the `EventRecord.Properties` collection (`EventData/Data` XML nodes)
- nvidia-smi cannot report SM count on consumer GPUs — the Vulkan `VK_NV_shader_sm_builtins` extension is queried instead, iterating physical devices to skip integrated GPUs
- Consumer GPUs report serial number as "0", so UUID is used for identification
- Kernel minidumps use the PAGEDU64 signature (not MDMP) with the bugcheck code at offset 0x38

## Disclaimer

This tool is provided as-is with no warranty. It reads system logs and crash dumps — it does not modify any system files, drivers, or settings.

## License

[GPL v3](LICENSE)
