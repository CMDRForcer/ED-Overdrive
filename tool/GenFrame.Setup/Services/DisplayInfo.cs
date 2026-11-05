using System.Runtime.InteropServices;

namespace GenFrame.Setup.Services;

// Whether Windows HDR ("Use HDR") is on, read from the display configuration (no window or device needed).
public static class DisplayInfo
{
    // True when any active display runs in HDR. Null when Windows does not say (old system, no display).
    public static bool? HdrEnabled()
    {
        try {
            if (GetDisplayConfigBufferSizes(OnlyActivePaths, out var pathCount, out var modeCount) != 0) {
                return null;
            }
            var paths = new PathInfo[pathCount];
            var modes = new byte[modeCount * ModeInfoSize];
            if (QueryDisplayConfig(OnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0) {
                return null;
            }

            bool? result = null;
            foreach (var path in paths.Take((int)pathCount)) {
                var info = new AdvancedColorInfo {
                    Type = GetAdvancedColorInfo,
                    Size = (uint)Marshal.SizeOf<AdvancedColorInfo>(),
                    AdapterId = path.TargetAdapterId,
                    Id = path.TargetId,
                };
                if (DisplayConfigGetDeviceInfo(ref info) != 0) {
                    continue;
                }
                // Bit 1: advanced colour enabled, bit 2: wide colour enforced (that is SDR with wide gamut, not HDR).
                var hdr = (info.Value & 0b10) != 0 && (info.Value & 0b100) == 0;
                result = (result ?? false) || hdr;
            }
            return result;
        }
        catch (Exception) {
            return null;
        }
    }

    const uint OnlyActivePaths = 2;
    const int GetAdvancedColorInfo = 9;
    const int ModeInfoSize = 64;

    [StructLayout(LayoutKind.Sequential)]
    struct Luid { public uint Low; public int High; }

    // DISPLAYCONFIG_PATH_INFO (72 bytes); only the target's adapter and id are used.
    [StructLayout(LayoutKind.Sequential)]
    struct PathInfo
    {
        public Luid SourceAdapterId;
        public uint SourceId, SourceModeIndex, SourceFlags;
        public Luid TargetAdapterId;
        public uint TargetId, TargetModeIndex;
        public int OutputTechnology, Rotation, Scaling;
        public uint RefreshNumerator, RefreshDenominator;
        public int ScanLineOrdering, TargetAvailable;
        public uint TargetFlags, Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct AdvancedColorInfo
    {
        public int Type;
        public uint Size;
        public Luid AdapterId;
        public uint Id;
        public uint Value;
        public int ColorEncoding;
        public uint BitsPerColorChannel;
    }

    [DllImport("user32.dll")]
    static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] PathInfo[] paths,
        ref uint modeCount, [Out] byte[] modes, IntPtr topologyId);

    [DllImport("user32.dll")]
    static extern int DisplayConfigGetDeviceInfo(ref AdvancedColorInfo info);
}
