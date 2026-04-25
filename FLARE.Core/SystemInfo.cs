using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace FLARE.Core;

public record SystemInfo(
    string BiosVendor,
    string BiosVersion,
    string BiosReleaseDate,
    string BoardManufacturer,
    string BoardProduct,
    string BoardVersion,
    string SystemManufacturer,
    string SystemProductName,
    string ProcessorName,
    ulong TotalMemoryBytes)
{
    public string TotalMemoryFormatted => FormatBytes(TotalMemoryBytes);

    internal static string FormatBytes(ulong bytes)
    {
        const double gb = 1024.0 * 1024.0 * 1024.0;
        return bytes == 0
            ? "(unknown)"
            : string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:F1} GB", bytes / gb);
    }

    public static SystemInfo Collect(Action<string>? log = null, CancellationToken ct = default, CollectorHealth? health = null)
    {
        ct.ThrowIfCancellationRequested();
        string biosVendor = "", biosVersion = "", biosDate = "";
        string boardMfr = "", boardProduct = "", boardVersion = "";
        string sysMfr = "", sysProduct = "";
        string cpu = "";
        ulong ram = 0;

        try
        {
            using var biosKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            if (biosKey != null)
            {
                biosVendor   = biosKey.GetValue("BIOSVendor") as string ?? "";
                biosVersion  = biosKey.GetValue("BIOSVersion") as string ?? "";
                biosDate     = biosKey.GetValue("BIOSReleaseDate") as string ?? "";
                boardMfr     = biosKey.GetValue("BaseBoardManufacturer") as string ?? "";
                boardProduct = biosKey.GetValue("BaseBoardProduct") as string ?? "";
                boardVersion = biosKey.GetValue("BaseBoardVersion") as string ?? "";
                sysMfr       = biosKey.GetValue("SystemManufacturer") as string ?? "";
                sysProduct   = biosKey.GetValue("SystemProductName") as string ?? "";
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"Warning: Could not read BIOS registry: {ex.Message}");
            health?.Failure("BIOS registry", ex.Message);
        }

        try
        {
            using var cpuKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            cpu = cpuKey?.GetValue("ProcessorNameString") as string ?? "";
            cpu = cpu.Trim();
        }
        catch (Exception ex)
        {
            log?.Invoke($"Warning: Could not read CPU registry: {ex.Message}");
            health?.Failure("CPU registry", ex.Message);
        }

        try
        {
            var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (GlobalMemoryStatusEx(ref status))
                ram = status.ullTotalPhys;
        }
        catch (Exception ex)
        {
            log?.Invoke($"Warning: Could not query memory status: {ex.Message}");
            health?.Failure("GlobalMemoryStatusEx", ex.Message);
        }

        return new SystemInfo(biosVendor, biosVersion, biosDate,
            boardMfr, boardProduct, boardVersion,
            sysMfr, sysProduct,
            cpu, ram);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);
}
