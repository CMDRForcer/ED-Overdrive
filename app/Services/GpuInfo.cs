using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace GenFrame.Setup.Services;

public enum GpuVendor { Unknown, Nvidia, Amd, Intel }

// One display adapter. VramGb is null when the driver does not say.
public sealed partial record Adapter(string Name, double? VramGb)
{
    // 4 for "RTX 4070 Ti", 0 for anything that is not a four-digit RTX.
    public int RtxSeries => RtxName().Match(Name) is { Success: true } m ? m.Groups[1].Value[0] - '0' : 0;

    // 70 for "RTX 4070 Ti", 0 for anything that is not a four-digit RTX.
    public int RtxClass => RtxName().Match(Name) is { Success: true } m ? int.Parse(m.Groups[1].Value[2..]) : 0;

    [GeneratedRegex(@"RTX\s*(\d{4})\b")]
    private static partial Regex RtxName();
}

// The vendor of the GPU driving the main display, read from the display devices (no driver call), and the adapters
// with their memory. Everything here only reads.
public static class GpuInfo
{
    public static GpuVendor PrimaryVendor()
    {
        try {
            var device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
            for (uint i = 0; EnumDisplayDevicesW(null, i, ref device, 0); i++) {
                if ((device.StateFlags & PrimaryDevice) != 0) {
                    return VendorOf(device.DeviceId);
                }
                device = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
            }
        }
        catch (Exception) {
        }
        return GpuVendor.Unknown;
    }

    // The display adapters with their video memory, read from the driver's class key (read only). The memory is
    // HardwareInformation.qwMemorySize (QWORD, newer drivers) or HardwareInformation.MemorySize (32 bit, capped at 4 GB).
    public static List<Adapter> Adapters()
    {
        var list = new List<Adapter>();
        try {
            using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            foreach (var name in cls?.GetSubKeyNames().Where(n => n.All(char.IsAsciiDigit)) ?? []) {
                using var key = cls!.OpenSubKey(name);
                if (key?.GetValue("DriverDesc") is not string description) {
                    continue;
                }
                var bytes = key.GetValue("HardwareInformation.qwMemorySize") switch {
                    long q => q,
                    byte[] b when b.Length >= 8 => BitConverter.ToInt64(b, 0),
                    _ => key.GetValue("HardwareInformation.MemorySize") switch {
                        int d => (uint)d,
                        byte[] b when b.Length >= 4 => BitConverter.ToUInt32(b, 0),
                        _ => 0L,
                    },
                };
                list.Add(new Adapter(description, bytes > 0 ? Math.Round(bytes / (1024.0 * 1024 * 1024)) : null));
            }
        }
        catch (Exception) {
        }
        return list;
    }

    // The card games run on: the one with the most memory (an integrated GPU has little or none).
    public static Adapter? GameAdapter() => Adapters().OrderByDescending(a => a.VramGb ?? 0).FirstOrDefault();

    // "PCI\VEN_10DE&DEV_..." : 10DE NVIDIA, 1002 AMD, 8086 Intel.
    static GpuVendor VendorOf(string id) =>
        id.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Nvidia
        : id.Contains("VEN_1002", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Amd
        : id.Contains("VEN_8086", StringComparison.OrdinalIgnoreCase) ? GpuVendor.Intel
        : GpuVendor.Unknown;

    const int PrimaryDevice = 4; // DISPLAY_DEVICE_PRIMARY_DEVICE

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool EnumDisplayDevicesW(string? device, uint index, ref DisplayDevice displayDevice, uint flags);
}
