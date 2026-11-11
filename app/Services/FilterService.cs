using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace GenFrame.Setup.Services;

public enum FilterVariant { Natural, Vivid, Performance, Dlss5 }

// The SDR filter set of docs/plan.md: only the shaders it needs, from their original sources
// (same URLs as the ReShade installer, see docs/tool-research.md), plus one preset file per variant.
public sealed class FilterService(GameInstall game, Action<string> log)
{
    // Folders: include trees below Shaders/ that are copied with their structure (lilium includes by relative path).
    sealed record Package(string Name, string Url, string[] Shaders, string[] Textures, string[]? Folders = null);

    static readonly Package[] Packages = [
        new("ReShade-Standard (Deband)", "https://github.com/crosire/reshade-shaders/archive/slim.zip",
            ["Deband.fx", "ReShade.fxh", "ReShadeUI.fxh"], []),
        new("prod80 (Cinetools LUT)", "https://github.com/prod80/prod80-ReShade-Repository/archive/master.zip",
            ["PD80_02_Cinetools_LUT.fx", "PD80_LUT_v2.fxh", "PD80_00_Noise_Samplers.fxh", "PD80_00_Color_Spaces.fxh"],
            ["pd80_cinelut.png", "pd80_bluenoise.png", "pd80_bluenoise_rgba.png", "pd80_gaussnoise.png"]),
        new("Insane-Shaders (LocalContrastCS)", "https://github.com/LordOfLunacy/Insane-Shaders/archive/master.zip",
            ["LocalContrastCS.fx"], []),
        new("SweetFX (CAS, Vibrance, Fake-HDR, Film Grain)", "https://github.com/CeeJayDK/SweetFX/archive/master.zip",
            ["CAS.fx", "Vibrance.fx", "FakeHDR.fx", "FilmGrain.fx"], []),
        new("Lilium (Schwarzwert-Fix)", "https://github.com/EndlesslyFlowering/ReShade_HDR_shaders/archive/master.zip",
            ["lilium__sdr_trc_fix.fx"], [], ["lilium__include"]),
        new("qUINT (MXAO, Bloom, Tiefenunschärfe)", "https://github.com/martymcmodding/qUINT/archive/master.zip",
            ["qUINT_mxao.fx", "qUINT_bloom.fx", "qUINT_dof.fx", "qUINT_common.fxh"], []),
    ];

    public static string PresetFile(FilterVariant variant) => variant switch {
        FilterVariant.Natural => "GenFrame_Natuerlich.ini",
        FilterVariant.Vivid => "GenFrame_Lebendig.ini",
        FilterVariant.Dlss5 => "GenFrame_DLSS5.ini",
        _ => "GenFrame_Leistung.ini",
    };

    public static string Describe(FilterVariant variant) => variant switch {
        FilterVariant.Natural => "Deband, Schärfe",
        FilterVariant.Vivid => "Deband, Farbe, Klarheit, Vibrance, Schärfe",
        FilterVariant.Dlss5 => "Deband, leichte Schärfe – Farbe und Klarheit macht DLSS 5",
        _ => "nur Schärfe",
    };

    static readonly HttpClient http = CreateClient();

    static string DownloadFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GenFrame", "Downloads", "shaders");

    public async Task Install(FilterVariant active)
    {
        if (GameState.IsRunning()) {
            throw new InvalidOperationException("Elite läuft. Bitte das Spiel beenden und erneut versuchen.");
        }
        Directory.CreateDirectory(DownloadFolder);

        var shaders = Path.Combine(game.Folder, "reshade-shaders", "Shaders");
        var textures = Path.Combine(game.Folder, "reshade-shaders", "Textures");
        Directory.CreateDirectory(shaders);
        Directory.CreateDirectory(textures);

        foreach (var package in Packages) {
            var zipFile = Path.Combine(DownloadFolder, package.Url.Split('/')[4] + ".zip");
            log($"Lade {package.Name} …");
            await DownloadTo(package.Url, zipFile);

            using var zip = ZipFile.OpenRead(zipFile);
            Extract(zip, "/Shaders/", package.Shaders, shaders);
            Extract(zip, "/Textures/", package.Textures, textures);
            foreach (var folder in package.Folders ?? []) {
                ExtractTree(zip, "/Shaders/" + folder + "/", Path.Combine(shaders, folder));
            }
        }
        log("Shader installiert (nur die des Filter-Sets).");

        // Existing presets hold the user's overlay changes: only missing ones are written.
        foreach (var variant in Enum.GetValues<FilterVariant>()) {
            var file = Path.Combine(game.Folder, PresetFile(variant));
            if (!File.Exists(file)) {
                File.WriteAllText(file, Preset(variant), new UTF8Encoding(false));
                log($"{PresetFile(variant)} angelegt.");
            }
        }
        SetActive(active);
        log($"Aktiv: {PresetFile(active)}. Deine Einstellungen in vorhandenen Presets bleiben.");
    }

    // Puts one variant back to the start values of docs/plan.md.
    public void RestoreDefaults(FilterVariant variant)
    {
        if (GameState.IsRunning()) {
            throw new InvalidOperationException("Elite läuft. Bitte das Spiel beenden und erneut versuchen.");
        }
        File.WriteAllText(Path.Combine(game.Folder, PresetFile(variant)), Preset(variant), new UTF8Encoding(false));
        log($"{PresetFile(variant)} auf Startwerte zurückgesetzt.");
    }

    // Elite stores depth reversed. MXAO and depth of field need this; set it when missing, never override the user's.
    public const string DepthDefinitions =
        "RESHADE_DEPTH_LINEARIZATION_FAR_PLANE=1000.0,RESHADE_DEPTH_INPUT_IS_UPSIDE_DOWN=0,RESHADE_DEPTH_INPUT_IS_REVERSED=1,RESHADE_DEPTH_INPUT_IS_LOGARITHMIC=0";

    public void SetActive(FilterVariant variant)
    {
        // A variant added after the filter set was installed (DLSS 5) gets its start values here.
        var preset = Path.Combine(game.Folder, PresetFile(variant));
        if (!File.Exists(preset)) {
            File.WriteAllText(preset, Preset(variant), new UTF8Encoding(false));
            log($"{PresetFile(variant)} angelegt.");
        }
        var path = Path.Combine(game.Folder, "ReShade.ini");
        var ini = File.Exists(path) ? IniFile.Load(path) : new IniFile();
        ini.Set("GENERAL", "PresetPath", @".\" + PresetFile(variant));
        // Load all effects (only the set's eleven are installed): optional ones can be ticked right away,
        // and nothing is loaded late and appended to the end of the order.
        ini.Set("GENERAL", "SkipLoadingDisabledEffects", "0");
        if (string.IsNullOrEmpty(ini.Get("GENERAL", "PreprocessorDefinitions"))) {
            ini.Set("GENERAL", "PreprocessorDefinitions", DepthDefinitions);
        }
        ini.Save(path);
        WriteFgWindowConfig(game);
        SyncBlackLevelFix();
    }

    const string TrcFixFile = "lilium__sdr_trc_fix.fx";
    const string TrcFixEntry = "lilium__sdr_trc_fix@" + TrcFixFile;

    // The black level fix belongs to Windows HDR (see Preset): switch it in every EDAA preset to match.
    // The rest of each preset stays as the user left it.
    public void SyncBlackLevelFix()
    {
        if (DisplayInfo.HdrEnabled() is not { } hdr) {
            log("Windows-HDR-Status unbekannt, Schwarzwert-Fix unverändert.");
            return;
        }
        foreach (var variant in Enum.GetValues<FilterVariant>()) {
            var file = Path.Combine(game.Folder, PresetFile(variant));
            if (!File.Exists(file) || BlackLevelFixOn(file) == hdr) {
                continue;
            }
            var text = File.ReadAllText(file);
            text = Regex.Replace(text, @"(?m)^Techniques=(.*?)\r?$", m => {
                var list = m.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries).Where(t => t != TrcFixEntry).ToList();
                if (hdr) {
                    list.Add(TrcFixEntry);
                }
                return "Techniques=" + string.Join(",", list) + (m.Value.EndsWith('\r') ? "\r" : "");
            });
            File.WriteAllText(file, text, new UTF8Encoding(false));
            log($"{PresetFile(variant)}: Schwarzwert-Fix {(hdr ? "an" : "aus")} (Windows-HDR {(hdr ? "an" : "aus")}).");
        }
    }

    public static bool BlackLevelFixOn(string presetFile) =>
        File.ReadLines(presetFile).FirstOrDefault(l => l.StartsWith("Techniques="))?.Split('=', 2)[1]
            .Split(',').Contains(TrcFixEntry) == true;

    const string FgWindowPreset = "GenFrame_FGFenster.ini";

    // With frame generation ReShade runs a second runtime on EDAA's D3D12 window and reads
    // ReShade2.ini for it. Effects cannot run there (pipeline creation fails on reload, 03.11.2026), so it
    // gets an empty preset of its own: nothing to load, nothing to fail. The filters run in the D3D11 runtime.
    public static void WriteFgWindowConfig(GameInstall game)
    {
        File.WriteAllText(Path.Combine(game.Folder, FgWindowPreset),
            "PreprocessorDefinitions=\r\nTechniques=\r\nTechniqueSorting=\r\n", new UTF8Encoding(false));

        var path = Path.Combine(game.Folder, "ReShade2.ini");
        var ini = File.Exists(path) ? IniFile.Load(path) : new IniFile();
        ini.Set("GENERAL", "EffectSearchPaths", @".\reshade-shaders\Shaders\**");
        ini.Set("GENERAL", "TextureSearchPaths", @".\reshade-shaders\Textures\**");
        ini.Set("GENERAL", "PresetPath", @".\" + FgWindowPreset);
        ini.Set("GENERAL", "SkipLoadingDisabledEffects", "1");
        ini.Set("INPUT", "KeyOverlay", "36,0,0,0");
        // Without it this runtime greets with "ReShade was installed successfully" on every start.
        ini.Set("OVERLAY", "TutorialProgress", "4");
        ini.Save(path);
    }

    public static FilterVariant? Active(GameInstall game)
    {
        var path = Path.Combine(game.Folder, "ReShade.ini");
        var preset = File.Exists(path) ? IniFile.Load(path).Get("GENERAL", "PresetPath") : null;
        return Enum.GetValues<FilterVariant>().Cast<FilterVariant?>()
            .FirstOrDefault(v => preset != null && preset.EndsWith(PresetFile(v!.Value), StringComparison.OrdinalIgnoreCase));
    }

    // Takes the named files from wherever they sit below the package's Shaders/Textures folder
    // (SweetFX keeps its effects in a subfolder) and puts them flat into the target.
    static void Extract(ZipArchive zip, string folder, string[] names, string target)
    {
        foreach (var name in names) {
            var entry = zip.Entries.FirstOrDefault(e => e.FullName.Contains(folder) && e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException($"{name} fehlt im Shader-Paket.");
            entry.ExtractToFile(Path.Combine(target, name), overwrite: true);
        }
    }

    // Keeps a folder of the package with its structure below the target.
    static void ExtractTree(ZipArchive zip, string folder, string target)
    {
        var found = false;
        foreach (var entry in zip.Entries.Where(e => e.Name.Length > 0)) {
            var at = entry.FullName.IndexOf(folder, StringComparison.Ordinal);
            if (at < 0) {
                continue;
            }
            var file = Path.Combine(target, entry.FullName[(at + folder.Length)..].Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            entry.ExtractToFile(file, overwrite: true);
            found = true;
        }
        if (!found) {
            throw new InvalidDataException($"{folder.Trim('/')} fehlt im Shader-Paket.");
        }
    }

    // Start values from docs/plan.md, order = execution order (sharpening last, only grain and the
    // black level fix after it). Optional effects are in the list but off: loaded, but not rendered
    // until ticked in the overlay. AO and depth of field need the depth buffer and
    // clash with the HUD/frame generation, hence never on by default.
    static string Preset(FilterVariant variant)
    {
        var full = variant != FilterVariant.Performance;
        var vivid = variant == FilterVariant.Vivid;
        var effects = new List<(string Technique, string File, bool On, (string Key, string Value)[] Values)> {
            ("MXAO", "qUINT_mxao.fx", false, []),
            ("Deband", "Deband.fx", full, [("iterations", "1"), ("range", "24.000000")]),
            ("prod80_02_Cinetools_LUT", "PD80_02_Cinetools_LUT.fx", vivid, [("PD80_LutSelector", "0"), ("PD80_Intensity", F(0.40))]),
            ("LocalContrastCS", "LocalContrastCS.fx", vivid, [("Strength", F(0.35))]),
            ("Vibrance", "Vibrance.fx", vivid, [("Vibrance", F(0.12))]),
            ("HDR", "FakeHDR.fx", false, []),
            ("Bloom", "qUINT_bloom.fx", false, []),
            ("ADOF", "qUINT_dof.fx", false, []),
            // DLSS 5 sharpens and adds contrast itself: CAS stays, but lighter.
            ("ContrastAdaptiveSharpen", "CAS.fx", true, [("Contrast", F(0)), ("Sharpening", F(variant == FilterVariant.Dlss5 ? 0.25 : 0.40))]),
            ("FilmGrain", "FilmGrain.fx", false, []),
            // With Windows HDR on, Windows shows SDR games with the sRGB curve, but they are made for gamma 2.2:
            // blacks come out grey. Decode as power 2.2, encode as sRGB. Without HDR it would only darken.
            ("lilium__sdr_trc_fix", TrcFixFile, DisplayInfo.HdrEnabled() == true,
                [("INPUT_TRC", "0"), ("INPUT_POWER_GAMMA", F(2.2)), ("TARGET_TRC", "0"), ("USE_BT1886", "0")]),
        };

        static string Names(IEnumerable<(string Technique, string File, bool On, (string, string)[] Values)> list) =>
            string.Join(",", list.Select(e => $"{e.Technique}@{e.File}"));
        var text = new StringBuilder();
        text.Append($"PreprocessorDefinitions=\r\nTechniques={Names(effects.Where(e => e.On))}\r\nTechniqueSorting={Names(effects)}\r\n");
        foreach (var e in effects.Where(e => e.Values.Length > 0).OrderBy(e => e.File, StringComparer.OrdinalIgnoreCase)) {
            text.Append($"\r\n[{e.File}]\r\n");
            foreach (var (key, value) in e.Values) {
                text.Append($"{key}={value}\r\n");
            }
        }
        return text.ToString();
    }

    static string F(double value) => value.ToString("0.000000", CultureInfo.InvariantCulture);

    async Task DownloadTo(string url, string file)
    {
        var temp = file + ".part";
        using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead)) {
            response.EnsureSuccessStatusCode();
            await using var output = File.Create(temp);
            await response.Content.CopyToAsync(output);
        }
        File.Move(temp, file, overwrite: true);
    }

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ED-Overdrive/0.1");
        return client;
    }
}
