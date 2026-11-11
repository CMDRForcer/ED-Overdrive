using System.Diagnostics;
using System.IO;

namespace GenFrame.Setup.Services;

public enum ComponentState { Missing, Installed, Disabled }

public sealed record Component(string Name, ComponentState State, string Detail, bool Foreign);

// Read-only look at what is installed in the game folder. Foreign components (EDHM, RenoDX)
// are only reported, never changed.
public static class InstallInfo
{
    public static List<Component> Scan(string gameFolder)
    {
        var disabled = DisabledAddons(Path.Combine(gameFolder, "ReShade.ini"));
        var list = new List<Component>();

        var dxgi = Path.Combine(gameFolder, "dxgi.dll");
        if (File.Exists(dxgi) && FileVersionInfo.GetVersionInfo(dxgi).ProductName?.Contains("ReShade") == true) {
            list.Add(new Component("ReShade", ComponentState.Installed, Version(dxgi), false));
        }
        else {
            list.Add(new Component("ReShade", ComponentState.Missing, "dxgi.dll fehlt oder ist nicht ReShade", false));
        }

        var addon = new[] { "GenFrame.addon64", "EDAA.addon64" }.Select(n => Path.Combine(gameFolder, n)).FirstOrDefault(File.Exists);
        list.Add(addon != null
            ? new Component("EDAA", ComponentState.Installed, Path.GetFileName(addon), false)
            : new Component("EDAA", ComponentState.Missing, "Add-on nicht gefunden", false));

        var streamline = Path.Combine(gameFolder, "sl.interposer.dll");
        list.Add(File.Exists(streamline)
            ? new Component("Streamline", ComponentState.Installed, Version(streamline), false)
            : new Component("Streamline", ComponentState.Missing, "sl.*.dll fehlen", false));

        var d3d11 = Path.Combine(gameFolder, "d3d11.dll");
        if (File.Exists(d3d11) && File.Exists(Path.Combine(gameFolder, "d3dx.ini"))) {
            list.Add(new Component("EDHM / 3DMigoto", ComponentState.Installed, "wird nicht verändert", true));
        }

        foreach (var renodx in Directory.EnumerateFiles(gameFolder, "renodx-*.addon64")) {
            var off = disabled.Any(d => d.EndsWith("@" + Path.GetFileName(renodx), StringComparison.OrdinalIgnoreCase));
            list.Add(new Component("RenoDX", off ? ComponentState.Disabled : ComponentState.Installed,
                Path.GetFileName(renodx) + " · wird nicht verändert", true));
        }

        return list;
    }

    // What the game really loaded last time, from ReShade.log ("EDAA: loaded, version ..., build ...").
    public static string? LoadedVersion(string gameFolder)
    {
        var log = Path.Combine(gameFolder, "ReShade.log");
        if (!File.Exists(log)) {
            return null;
        }
        using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = reader.ReadLine()) != null) {
            var at = line.IndexOf("EDAA: loaded, version ", StringComparison.Ordinal);
            if (at >= 0) {
                return line[(at + "EDAA: loaded, version ".Length)..];
            }
        }
        return null;
    }

    static string Version(string file)
    {
        var v = FileVersionInfo.GetVersionInfo(file);
        // Some DLLs (Streamline) write their version as "2, 14, 16, 0".
        var text = v.ProductVersion ?? v.FileVersion;
        return text == null ? "unbekannte Version" : text.Replace(", ", ".").Replace(',', '.');
    }

    static List<string> DisabledAddons(string reshadeIni)
    {
        if (!File.Exists(reshadeIni)) {
            return [];
        }
        var line = File.ReadLines(reshadeIni).FirstOrDefault(l => l.StartsWith("DisabledAddons="));
        return line == null ? [] : line["DisabledAddons=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
    }
}
