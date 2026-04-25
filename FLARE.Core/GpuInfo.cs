using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;

namespace FLARE.Core;

public record GpuInfo(
    string Name,
    string DriverVersion,
    string VbiosVersion,
    string Serial,
    string Uuid,
    string PciId,
    int SmCount,
    string MemoryTotal,
    int PcieCurrentGen,
    int PcieMaxGen,
    int PcieCurrentWidth,
    int PcieMaxWidth,
    long Bar1TotalMib,
    int NvidiaDeviceCount = 1
)
{
    // Pin to the System32 copy (installed by the NVIDIA driver): different nvidia-smi
    // versions emit subtly different -q section layouts, and we want the one that
    // matches the driver FLARE is reporting on.
    public static string? FindNvidiaSmi(Action<string>? log = null)
    {
        string[] trustedPaths = [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvidia-smi.exe"),
        ];

        return FindNvidiaSmiInTrustedPaths(trustedPaths, File.Exists);
    }

    internal static string? FindNvidiaSmiInTrustedPaths(string[] candidatePaths, Func<string, bool> exists)
    {
        foreach (var p in candidatePaths)
            if (exists(p)) return p;
        return null;
    }

    public static unsafe GpuInfo Collect(Action<string>? log = null, CancellationToken ct = default, CollectorHealth? health = null)
    {
        string name = "", driver = "", vbios = "", serial = "", uuid = "", pci = "", mem = "";
        int pcieCurGen = 0, pcieMaxGen = 0, pcieCurWidth = 0, pcieMaxWidth = 0;
        long bar1TotalMib = 0;
        int nvidiaDeviceCount = 1;

        var nvidiaSmi = FindNvidiaSmi(log);
        if (nvidiaSmi == null)
        {
            log?.Invoke("Warning: nvidia-smi not found at System32\\nvidia-smi.exe (NVIDIA driver not installed,");
            log?.Invoke("         or installed to a non-standard location). GPU identification will be empty;");
            log?.Invoke("         Vulkan SM-count query and the event-log pipeline proceed independently.");
            health?.Failure("nvidia-smi", "not found at System32\\nvidia-smi.exe; GPU identification fields will be empty");
        }
        else
        {
            try
            {
                var csv = ProcessRunner.RunWithLog(nvidiaSmi, log, ct,
                    "--query-gpu=name,driver_version,vbios_version,serial,uuid,pci.bus_id,memory.total",
                    "--format=csv,noheader,nounits");

                if (!string.IsNullOrEmpty(csv))
                {
                    var rows = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    nvidiaDeviceCount = rows.Length > 0 ? rows.Length : 1;

                    var parts = rows[0].Split(',').Select(p => p.Trim()).ToArray();
                    if (parts.Length >= 7)
                    {
                        name = parts[0]; driver = parts[1]; vbios = parts[2];
                        serial = parts[3]; uuid = parts[4]; pci = parts[5]; mem = parts[6] + " MB";
                    }
                    else
                    {
                        log?.Invoke($"Warning: nvidia-smi returned {parts.Length} fields, expected 7.");
                        health?.Canary("nvidia-smi --query-gpu", $"returned {parts.Length} fields, expected 7; GPU identification may be partial");
                    }
                }
                else
                {
                    log?.Invoke("Warning: nvidia-smi produced no output (killed by watchdog timeout, not installed, or no NVIDIA GPU).");
                    health?.Failure("nvidia-smi", "produced no output (killed by watchdog timeout, not installed, or no NVIDIA GPU); GPU identification fields will be empty");
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"Warning: nvidia-smi query failed: {ex.Message}");
                health?.Failure("nvidia-smi --query-gpu", ex.Message);
            }

            try
            {
                var queryOutput = ProcessRunner.RunWithLog(nvidiaSmi, log, ct, "-q");
                if (!string.IsNullOrEmpty(queryOutput))
                {
                    (pcieCurGen, pcieMaxGen, pcieCurWidth, pcieMaxWidth) = ParsePcieFromQueryOutput(queryOutput);
                    bar1TotalMib = ParseBar1TotalMibFromQueryOutput(queryOutput);
                    WarnIfQueryLayoutDrift(queryOutput,
                        pcieCurGen, pcieMaxGen, pcieCurWidth, pcieMaxWidth,
                        bar1TotalMib, log, health);
                }
                else
                {
                    health?.Failure("nvidia-smi -q", "produced no output (killed by watchdog timeout or failed to start); PCIe link and BAR1 fields unavailable");
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"Warning: nvidia-smi -q query failed: {ex.Message}");
                health?.Failure("nvidia-smi -q", ex.Message);
            }
        }

        int smCount = QuerySmCountViaVulkan(log, ref nvidiaDeviceCount, health);

        if (serial == "0" || serial == "[N/A]") serial = uuid;

        if (nvidiaDeviceCount > 1)
            log?.Invoke($"Warning: {nvidiaDeviceCount} NVIDIA GPUs detected; FLARE reports the first one only. Event log / crash data covers the whole system.");

        return new GpuInfo(name, driver, vbios, serial, uuid, pci, smCount, mem,
            pcieCurGen, pcieMaxGen, pcieCurWidth, pcieMaxWidth, bar1TotalMib,
            NvidiaDeviceCount: nvidiaDeviceCount);
    }

    internal static void WarnIfQueryLayoutDrift(
        string queryOutput,
        int pcieCurrentGen,
        int pcieMaxGen,
        int pcieCurrentWidth,
        int pcieMaxWidth,
        long bar1TotalMib,
        Action<string>? log,
        CollectorHealth? health)
    {
        var hasLinkBlock = queryOutput.Contains("GPU Link Info", StringComparison.OrdinalIgnoreCase);
        if (!hasLinkBlock)
        {
            const string msg = "'GPU Link Info' block not found; PCIe link state unavailable";
            log?.Invoke($"Warning: nvidia-smi -q layout: {msg}");
            health?.Canary("nvidia-smi -q layout", msg);
        }
        else if (pcieCurrentGen == 0 || pcieMaxGen == 0 || pcieCurrentWidth == 0 || pcieMaxWidth == 0)
        {
            const string msg = "'GPU Link Info' block found but PCIe link values could not be fully parsed";
            log?.Invoke($"Warning: nvidia-smi -q layout: {msg}");
            health?.Canary("nvidia-smi -q layout", msg);
        }

        var hasBar1Block = queryOutput.Contains("BAR1 Memory Usage", StringComparison.OrdinalIgnoreCase);
        if (!hasBar1Block)
        {
            const string msg = "'BAR1 Memory Usage' block not found; Resizable BAR status unavailable";
            log?.Invoke($"Warning: nvidia-smi -q layout: {msg}");
            health?.Canary("nvidia-smi -q layout", msg);
        }
        else if (bar1TotalMib == 0)
        {
            const string msg = "'BAR1 Memory Usage' block found but Total MiB value could not be parsed";
            log?.Invoke($"Warning: nvidia-smi -q layout: {msg}");
            health?.Canary("nvidia-smi -q layout", msg);
        }
    }

    // Used to infer Resizable BAR: ~256 MiB = off, several GB (= GPU memory) = on.
    internal static long ParseBar1TotalMibFromQueryOutput(string output)
    {
        var start = output.IndexOf("BAR1 Memory Usage", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return 0;
        var block = output.Substring(start);
        var totalMatch = Regex.Match(block, @"Total\s*:\s*(\d+)\s*MiB", RegexOptions.IgnoreCase);
        if (totalMatch.Success && long.TryParse(totalMatch.Groups[1].Value, out var mib))
            return mib;
        return 0;
    }

    internal static (int currentGen, int maxGen, int currentWidth, int maxWidth) ParsePcieFromQueryOutput(string output)
    {
        int currentGen = 0, maxGen = 0, currentWidth = 0, maxWidth = 0;

        var linkStart = output.IndexOf("GPU Link Info", StringComparison.OrdinalIgnoreCase);
        if (linkStart < 0) return (0, 0, 0, 0);

        var linkBlock = output.Substring(linkStart);

        var genSection = Regex.Match(linkBlock, @"PCIe Generation\s*(.+?)Link Width", RegexOptions.Singleline);
        if (genSection.Success)
        {
            var maxMatch = Regex.Match(genSection.Groups[1].Value, @"Max\s*:\s*(\d+)");
            var curMatch = Regex.Match(genSection.Groups[1].Value, @"Current\s*:\s*(\d+)");
            if (maxMatch.Success) int.TryParse(maxMatch.Groups[1].Value, out maxGen);
            if (curMatch.Success) int.TryParse(curMatch.Groups[1].Value, out currentGen);
        }

        var widthSection = Regex.Match(linkBlock, @"Link Width\s*(.+?)(?:\n\s*\n|\Z)", RegexOptions.Singleline);
        if (widthSection.Success)
        {
            var maxMatch = Regex.Match(widthSection.Groups[1].Value, @"Max\s*:\s*(\d+)x");
            var curMatch = Regex.Match(widthSection.Groups[1].Value, @"Current\s*:\s*(\d+)x");
            if (maxMatch.Success) int.TryParse(maxMatch.Groups[1].Value, out maxWidth);
            if (curMatch.Success) int.TryParse(curMatch.Groups[1].Value, out currentWidth);
        }

        return (currentGen, maxGen, currentWidth, maxWidth);
    }

    static unsafe int QuerySmCountViaVulkan(Action<string>? log, ref int nvidiaDeviceCount, CollectorHealth? health = null)
    {
        try
        {
            vkInitialize().CheckResult();
            VkUtf8ReadOnlyString appName = "FLARE"u8;
            VkUtf8ReadOnlyString engineName = "None"u8;
            VkApplicationInfo appInfo = new()
            {
                pApplicationName = appName, applicationVersion = new VkVersion(1, 0, 0),
                pEngineName = engineName, engineVersion = new VkVersion(1, 0, 0),
                apiVersion = VkVersion.Version_1_3
            };
            VkInstanceCreateInfo instInfo = new() { pApplicationInfo = &appInfo };
            vkCreateInstance(&instInfo, out VkInstance instance).CheckResult();
            var api = GetApi(instance);

            try
            {
                uint devCount = 0;
                api.vkEnumeratePhysicalDevices(&devCount, null).CheckResult();
                if (devCount == 0) return 0;

                Span<VkPhysicalDevice> devs = stackalloc VkPhysicalDevice[(int)devCount];
                api.vkEnumeratePhysicalDevices(devs).CheckResult();

                // iGPU + discrete: devs[0] may be the iGPU which lacks VK_NV_shader_sm_builtins
                // and reports 0 SMs. Pick the NVIDIA device explicitly.
                const uint NvidiaVendorId = 0x10DE;
                int nvidiaIndex = -1;
                int nvidiaCount = 0;
                for (int i = 0; i < (int)devCount; i++)
                {
                    VkPhysicalDeviceProperties props = new();
                    api.vkGetPhysicalDeviceProperties(devs[i], &props);
                    if (props.vendorID == NvidiaVendorId)
                    {
                        nvidiaCount++;
                        if (nvidiaIndex < 0) nvidiaIndex = i;
                    }
                }

                if (nvidiaCount > 0 && nvidiaCount != nvidiaDeviceCount)
                {
                    health?.Canary("adapter count",
                        $"nvidia-smi reported {nvidiaDeviceCount} NVIDIA adapter(s), Vulkan enumerated {nvidiaCount}; multi-GPU suppression will use the higher count");
                    if (nvidiaCount > nvidiaDeviceCount) nvidiaDeviceCount = nvidiaCount;
                }

                if (nvidiaIndex < 0)
                {
                    log?.Invoke($"Warning: no NVIDIA GPU found among {devCount} Vulkan physical device(s).");
                    health?.Failure("Vulkan SM-count", $"no NVIDIA GPU among {devCount} physical device(s); SM count unavailable");
                    return 0;
                }

                VkPhysicalDeviceShaderSMBuiltinsPropertiesNV smProps = new();
                VkPhysicalDeviceProperties2 props2 = new() { pNext = &smProps };
                api.vkGetPhysicalDeviceProperties2(devs[nvidiaIndex], &props2);

                return (int)smProps.shaderSMCount;
            }
            finally
            {
                api.vkDestroyInstance();
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"Warning: Vulkan SM count query failed: {ex.Message}");
            health?.Failure("Vulkan SM-count", ex.Message);
            return 0;
        }
    }
}
