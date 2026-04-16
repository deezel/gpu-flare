using System;
using System.Diagnostics;
using System.Linq;
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
    string MemoryTotal
)
{
    public static unsafe GpuInfo Collect(Action<string>? log = null)
    {
        string name = "", driver = "", vbios = "", serial = "", uuid = "", pci = "", mem = "";
        int smCount = 0;

        try
        {
            var csv = ProcessRunner.RunWithLog("nvidia-smi", log,
                "--query-gpu=name,driver_version,vbios_version,serial,uuid,pci.bus_id,memory.total",
                "--format=csv,noheader,nounits");

            if (!string.IsNullOrEmpty(csv))
            {
                var parts = csv.Split(',').Select(p => p.Trim()).ToArray();
                if (parts.Length >= 7)
                {
                    name = parts[0]; driver = parts[1]; vbios = parts[2];
                    serial = parts[3]; uuid = parts[4]; pci = parts[5]; mem = parts[6] + " MB";
                }
                else
                {
                    log?.Invoke($"Warning: nvidia-smi returned {parts.Length} fields, expected 7.");
                }
            }
            else
            {
                log?.Invoke("Warning: nvidia-smi produced no output (not installed or no NVIDIA GPU?).");
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"Warning: nvidia-smi query failed: {ex.Message}");
        }

        if (smCount == 0)
            smCount = QuerySmCountViaVulkan(log);

        if (serial == "0" || serial == "[N/A]") serial = uuid;

        return new GpuInfo(name, driver, vbios, serial, uuid, pci, smCount, mem);
    }

    static unsafe int QuerySmCountViaVulkan(Action<string>? log = null)
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

            uint devCount = 0;
            api.vkEnumeratePhysicalDevices(&devCount, null).CheckResult();
            if (devCount == 0) { api.vkDestroyInstance(); return 0; }

            Span<VkPhysicalDevice> devs = stackalloc VkPhysicalDevice[(int)devCount];
            api.vkEnumeratePhysicalDevices(devs).CheckResult();

            // Find the NVIDIA device — on systems with an iGPU (Intel/AMD) plus
            // discrete NVIDIA, devs[0] may be the iGPU which doesn't support
            // VK_NV_shader_sm_builtins and reports 0 SMs.
            const uint NvidiaVendorId = 0x10DE;
            int nvidiaIndex = -1;
            for (int i = 0; i < (int)devCount; i++)
            {
                VkPhysicalDeviceProperties props = new();
                api.vkGetPhysicalDeviceProperties(devs[i], &props);
                if (props.vendorID == NvidiaVendorId)
                {
                    nvidiaIndex = i;
                    break;
                }
            }

            if (nvidiaIndex < 0)
            {
                log?.Invoke($"Warning: no NVIDIA GPU found among {devCount} Vulkan physical device(s).");
                api.vkDestroyInstance();
                return 0;
            }

            VkPhysicalDeviceShaderSMBuiltinsPropertiesNV smProps = new();
            VkPhysicalDeviceProperties2 props2 = new() { pNext = &smProps };
            api.vkGetPhysicalDeviceProperties2(devs[nvidiaIndex], &props2);

            int count = (int)smProps.shaderSMCount;
            api.vkDestroyInstance();
            return count;
        }
        catch (Exception ex)
        {
            log?.Invoke($"Warning: Vulkan SM count query failed: {ex.Message}");
            return 0;
        }
    }
}
