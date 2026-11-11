using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace GenFrame.Setup.Services;

// Elite's profile in the NVIDIA driver, through NVAPI's DRS functions (IDs: docs/tool-research.md).
// Only frame generation's preset entries are ever written, and only to this one profile. Reading needs
// no rights; writing runs in a second, elevated copy of the program (App.OnStartup).
public static class NvDrs
{
    public const uint FgOverride = 0x10E41E03, FgPreset = 0x10E41DF1, StreamlineOverride = 0x10E41E06;
    public const string ApplyArgument = "--drs-apply";

    public const uint Recommended = 0x00FFFFFE;
    const string App = "EliteDangerous64.exe";
    const int SettingNotFound = -160, ExecutableNotFound = -166;
    const int SettingSize = 12320, ApplicationSize = 12296;
    const int IdOffset = 4100, LocationOffset = 4108, ValueOffset = 8220;

    static readonly uint[] Writable = [FgOverride, FgPreset, StreamlineOverride];

    public sealed record Entry(uint Id, uint? Own, uint Effective);

    public sealed record ProfileState(bool ProfileFound, List<Entry> Entries)
    {
        public Entry? Find(uint id) => Entries.FirstOrDefault(e => e.Id == id);
    }

    // What the page remembers: the values before the first change, and what the user asked for.
    public sealed record Memory(Dictionary<uint, uint?> Original, uint? Wanted);

    static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GenFrame");
    static string MemoryPath => Path.Combine(Folder, "presets.json");

    public static string Letter(uint value) => value == Recommended ? "empfohlen"
        : value is >= 1 and <= 18 ? ((char)('A' + value - 1)).ToString() : $"0x{value:X}";

    public static ProfileState Read()
    {
        using var session = new Session();
        var (profile, found) = session.FindProfile();
        if (!found) {
            return new ProfileState(false, []);
        }
        return new ProfileState(true, Writable.Select(id => session.ReadSetting(profile, id)).ToList());
    }

    public static Memory? LoadMemory()
    {
        try {
            return File.Exists(MemoryPath) ? JsonSerializer.Deserialize<Memory>(File.ReadAllText(MemoryPath)) : null;
        }
        catch (JsonException) {
            return null;
        }
    }

    // Sets the FG preset letter, the same three entries the NVIDIA App writes (seen in the profile on 05.11.2026).
    public static void SetFgPreset(uint value)
    {
        var state = Read();
        if (!state.ProfileFound) {
            throw new InvalidOperationException("Der Treiber hat kein Profil für Elite Dangerous. Das legt die App nicht selbst an.");
        }
        var original = LoadMemory()?.Original ?? state.Entries.ToDictionary(e => e.Id, e => e.Own);
        Directory.CreateDirectory(Folder);
        File.WriteAllText(MemoryPath, JsonSerializer.Serialize(new Memory(original, value)));
        RunElevated(new Dictionary<uint, uint?> { [FgOverride] = 1, [StreamlineOverride] = 1, [FgPreset] = value });
    }

    // Back to the values from before the first change; the memory goes with it.
    public static void Restore()
    {
        var memory = LoadMemory() ?? throw new InvalidOperationException("Es gibt keinen gemerkten Stand.");
        RunElevated(memory.Original);
        File.Delete(MemoryPath);
    }

    static void RunElevated(Dictionary<uint, uint?> values)
    {
        var request = Path.Combine(Folder, "drs-request.json");
        File.WriteAllText(request, JsonSerializer.Serialize(values));
        try {
            var start = new ProcessStartInfo(Environment.ProcessPath!, $"{ApplyArgument} \"{request}\"") {
                UseShellExecute = true,
                Verb = "runas",
            };
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Der Schreibschritt ließ sich nicht starten.");
            process.WaitForExit();
            if (process.ExitCode != 0) {
                throw new InvalidOperationException($"NVIDIA-Treiber hat den Schreibvorgang abgelehnt (Code {process.ExitCode}).");
            }
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223) {
            throw new OperationCanceledException("Die Administrator-Abfrage wurde abgelehnt, es wurde nichts geändert.");
        }
        finally {
            File.Delete(request);
        }
    }

    // Runs in the elevated copy. The request file is writable without rights, so only the known entries are accepted.
    public static int ApplyRequest(string requestFile)
    {
        try {
            var values = JsonSerializer.Deserialize<Dictionary<uint, uint?>>(File.ReadAllText(requestFile)) ?? [];
            if (values.Keys.Any(id => !Writable.Contains(id)) || values.Count == 0) {
                return values.Count == 0 ? 0 : 2;
            }
            using var session = new Session();
            var (profile, found) = session.FindProfile();
            if (!found) {
                return 3;
            }
            foreach (var (id, value) in values) {
                session.Write(profile, id, value);
            }
            session.Save();
            return 0;
        }
        catch (NvApiException e) {
            return e.Code;
        }
        catch (Exception) {
            return 1;
        }
    }

    sealed class NvApiException(string call, int code) : Exception($"{call}: NVAPI-Fehler {code}")
    {
        public int Code { get; } = code;
    }

    sealed class Session : IDisposable
    {
        readonly IntPtr handle;

        public Session()
        {
            Check("Initialize", Function<NoArgs>(0x0150E828)());
            Check("CreateSession", Function<CreateSession>(0x0694D52E)(out handle));
            Check("LoadSettings", Function<WithSession>(0x375DBD6B)(handle));
        }

        public (IntPtr Profile, bool Found) FindProfile()
        {
            var info = new byte[ApplicationSize];
            BitConverter.TryWriteBytes(info, ApplicationSize | 1 << 16);
            var result = Function<FindApplication>(0xEEE566B2)(handle, App, out var profile, info);
            if (result == ExecutableNotFound) {
                return (IntPtr.Zero, false);
            }
            Check("FindApplicationByName", result);
            return (profile, true);
        }

        public Entry ReadSetting(IntPtr profile, uint id)
        {
            var buffer = NewSetting(id);
            var result = Function<GetSetting>(0x73BF8338)(handle, profile, id, buffer);
            if (result == SettingNotFound) {
                return new Entry(id, null, 0);
            }
            Check("GetSetting", result);
            var value = BitConverter.ToUInt32(buffer, ValueOffset);
            // Location 0 is this profile; anything else comes from the global or default level.
            return new Entry(id, BitConverter.ToUInt32(buffer, LocationOffset) == 0 ? value : null, value);
        }

        public void Write(IntPtr profile, uint id, uint? value)
        {
            if (value is { } number) {
                var buffer = NewSetting(id);
                BitConverter.TryWriteBytes(buffer.AsSpan(ValueOffset), number);
                Check("SetSetting", Function<SetSetting>(0x577DD202)(handle, profile, buffer));
                return;
            }
            var result = Function<DeleteSetting>(0xE4A26362)(handle, profile, id);
            if (result != SettingNotFound) {
                Check("DeleteProfileSetting", result);
            }
        }

        public void Save() => Check("SaveSettings", Function<WithSession>(0xFCBC7E14)(handle));

        public void Dispose() => Function<WithSession>(0xDAD9CFF8)(handle);

        static byte[] NewSetting(uint id)
        {
            var buffer = new byte[SettingSize];
            BitConverter.TryWriteBytes(buffer, SettingSize | 1 << 16);
            BitConverter.TryWriteBytes(buffer.AsSpan(IdOffset), id);
            return buffer; // type 0 = a DWORD
        }

        static void Check(string call, int result)
        {
            if (result != 0) {
                throw new NvApiException(call, result);
            }
        }

        static T Function<T>(uint id) where T : Delegate
        {
            var pointer = QueryInterface(id);
            return pointer != IntPtr.Zero
                ? Marshal.GetDelegateForFunctionPointer<T>(pointer)
                : throw new NvApiException("QueryInterface", -3);
        }

        [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr QueryInterface(uint id);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int NoArgs();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int CreateSession(out IntPtr session);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int WithSession(IntPtr session);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        delegate int FindApplication(IntPtr session, [MarshalAs(UnmanagedType.LPWStr)] string app, out IntPtr profile, byte[] application);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GetSetting(IntPtr session, IntPtr profile, uint id, byte[] setting);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int SetSetting(IntPtr session, IntPtr profile, byte[] setting);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int DeleteSetting(IntPtr session, IntPtr profile, uint id);
    }
}
