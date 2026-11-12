using System.Globalization;
using System.IO;

namespace GenFrame.Setup.Services;

// One suggestion of the advisor. Apply is null when the advisor only reports (or nothing is to change).
public sealed record Advice(string Title, string Current, string Recommended, string Why, bool Fits, Action? Apply);

// What the advisor found about this PC.
public sealed record SystemFacts(Adapter? Gpu, int? Width, int? Height, int? RefreshRate, bool? Hdr);

// Suggests EDAA mode, frame limit, game setup and graphics level from the card, the screen and Elite's files.
// Suggest() only reads. An Apply changes files and expects the caller to have checked that Elite is closed and to
// have made the backup. Every rule says whether it was measured (RTX 4070 Ti, 1440p, 144 Hz) or is an assumption.
public sealed class Advisor(GameInstall game)
{
    static string GraphicsFolder => EliteCheck.DefaultGraphicsFolder;
    static string DisplayXml => Path.Combine(GraphicsFolder, "DisplaySettings.xml");
    static string CustomXml => Path.Combine(GraphicsFolder, "Custom.4.4.fxcfg");

    public static SystemFacts Facts() => new(GpuInfo.GameAdapter(),
        Whole(EliteCheck.XmlValue(DisplayXml, "ScreenWidth")), Whole(EliteCheck.XmlValue(DisplayXml, "ScreenHeight")),
        DisplayInfo.RefreshRate(), DisplayInfo.HdrEnabled());

    public List<Advice> Suggest(SystemFacts facts)
    {
        var list = new List<Advice>();
        var mode = RecommendedMode(facts.Gpu);
        list.Add(ModeAdvice(mode, facts.Gpu));
        if (mode is Mode.DlaaFg) {
            list.Add(FrameLimitAdvice(facts.RefreshRate));
        }
        var supersampling = SetupAdvice(facts, out var setupSetsGraphics);
        list.Add(supersampling);
        if (!setupSetsGraphics) {
            list.AddRange(GraphicsAdvice(facts.Gpu));
        }
        list.AddRange(CheckHints());
        return list;
    }

    // --- EDAA mode -------------------------------------------------------------------------------------------------

    enum Mode { DlaaFg, Dlaa, Taa }

    static Mode RecommendedMode(Adapter? gpu) =>
        Vendor.Amd || gpu is not { RtxSeries: >= 2 } ? Mode.Taa : gpu.RtxSeries >= 4 ? Mode.DlaaFg : Mode.Dlaa;

    static string Text(Mode mode) => mode switch {
        Mode.DlaaFg => "DLAA + FG",
        Mode.Dlaa => "Nur DLAA",
        _ => "EDAA TAA ohne FG",
    };

    // The values a mode sets; DLAA modes are the quick modes, TAA without FG has no quick mode.
    static Dictionary<string, string> Values(Mode mode) => mode switch {
        Mode.DlaaFg => AddonSettings.ModeValues(QuickMode.DlaaFg),
        Mode.Dlaa => AddonSettings.ModeValues(QuickMode.Dlaa),
        _ => new() {
            ["taa_enabled"] = "1", ["jitter_enabled"] = "1", ["aa_method"] = "0", ["fg_generate"] = "0", ["fg_present_test"] = "0",
        },
    };

    string CurrentModeText()
    {
        var quick = AddonSettings.CurrentMode(game);
        if (quick is { } q) {
            return q switch {
                QuickMode.DlaaFg => Text(Mode.DlaaFg),
                QuickMode.Dlaa => Text(Mode.Dlaa),
                QuickMode.TaaFg => "TAA + FG",
                _ => "Aus",
            };
        }
        return IsTaaOnly() ? Text(Mode.Taa) : "eigene Werte";
    }

    bool IsTaaOnly() => AddonSettings.Get(game, "taa_enabled") == "1" && AddonSettings.Get(game, "aa_method") == "0"
        && AddonSettings.Get(game, "fg_generate") != "1";

    Advice ModeAdvice(Mode mode, Adapter? gpu)
    {
        var fits = mode switch {
            Mode.DlaaFg => AddonSettings.CurrentMode(game) == QuickMode.DlaaFg,
            Mode.Dlaa => AddonSettings.CurrentMode(game) == QuickMode.Dlaa,
            _ => IsTaaOnly(),
        };
        var why = mode switch {
            Mode.DlaaFg => $"{gpu!.Name}: DLAA und DLSS Frame Generation laufen auf RTX 40/50. Beste Bildqualität bei doppelter Bildrate.",
            Mode.Dlaa => $"{gpu!.Name}: DLAA läuft ab RTX 20, DLSS Frame Generation erst ab RTX 40.",
            _ when Vendor.Amd => "Diese Fassung hat kein DLSS: EDAA TAA glättet auf jeder Karte.",
            _ => (gpu?.Name ?? "Grafikkarte nicht erkannt") + ": DLAA braucht eine RTX. EDAA TAA glättet auf jeder Karte.",
        };
        return new("EDAA-Modus", CurrentModeText(), Text(mode), why, fits,
            fits ? null : () => AddonSettings.Write(game, Values(mode)));
    }

    // --- Frame limit -----------------------------------------------------------------------------------------------

    Advice FrameLimitAdvice(int? hz)
    {
        var current = AddonSettings.Get(game, "fg_fps_limit") ?? "140";
        if (hz is not { } rate) {
            return new("Bildlimit mit FG", current, "unverändert", "Bildwiederholrate unbekannt, Windows meldet keine.", true, null);
        }
        var target = (rate - 4).ToString(CultureInfo.InvariantCulture);
        return new("Bildlimit mit FG", current == "0" ? "aus" : current, target,
            $"Knapp unter {rate} Hz, damit die Bilder gleichmäßig kommen. Annahme: gemessen ist nur 140 bei 144 Hz.",
            current == target, current == target ? null : () => AddonSettings.Write(game, new Dictionary<string, string> { ["fg_fps_limit"] = target }));
    }

    // --- Game setup / supersampling ---------------------------------------------------------------------------------

    // A strong card in up to 1440p plays the game setup "Supersampling 1,25" (measured), everything else renders at 1,0.
    Advice SetupAdvice(SystemFacts facts, out bool setupSetsGraphics)
    {
        setupSetsGraphics = false;
        var current = GameSetups.Current(game);
        if (current == GameSetup.Dlss5) {
            setupSetsGraphics = true;
            return new("Spiel-Setup", "DLSS 5", "DLSS 5", "Du spielst mit DLSS 5. Das Setup bringt Auflösung und Grafik selbst mit, der Berater lässt es so.", true, null);
        }

        var ss = Number(EliteCheck.XmlValue(CustomXml, "SSAAMultiplier"));
        var ssText = ss is { } v ? v.ToString("0.0#", CultureInfo.GetCultureInfo("de-DE")) : "unbekannt";
        var strong = Vendor.Nvidia && facts.Gpu is { RtxSeries: >= 4, RtxClass: >= 70 } && facts.Height is <= 1440;
        if (strong) {
            setupSetsGraphics = true;
            var fits = current == GameSetup.Supersampling;
            return new("Spiel-Setup", fits ? "Supersampling 1,25" : $"Supersampling {ssText}", "Supersampling 1,25",
                $"{facts.Gpu!.Name} in {facts.Height}p: Elite rendert größer, DLAA glättet, Grafik Ultra. " +
                "Gemessen auf einer RTX 4070 Ti in 1440p: 70 echte → 140 Bilder/s. Stellt Auflösung, Grafik und Filter zusammen ein.",
                fits, fits || !File.Exists(CustomXml) ? null : () => GameSetups.Apply(game, GameSetup.Supersampling, _ => { }));
        }

        var why = facts.Height is > 1440
            ? $"In {facts.Height}p kostet größeres Rendern zu viel. 1,0 oder kleiner (EDAA skaliert hoch)."
            : "Größeres Rendern kostet viel Leistung, auf schwächeren Karten bleibt es bei 1,0 oder kleiner (EDAA skaliert hoch). Annahme, gemessen nur auf einer RTX 4070 Ti.";
        var ok = ss is <= 1.0001;
        return new("Supersampling", ssText, "1,0 oder kleiner", why, ok,
            ok || ss == null ? null : () => EliteCheck.SetXml(CustomXml, "SSAAMultiplier", "1.000000"));
    }

    // --- Graphics level ---------------------------------------------------------------------------------------------

    IEnumerable<Advice> GraphicsAdvice(Adapter? gpu)
    {
        var options = new GraphicsOptions(GraphicsFolder);
        if (!options.Available) {
            yield return new("Grafikstufe", "unbekannt", "–",
                "Custom.4.4.fxcfg fehlt. Stelle die Grafik im Spiel einmal auf „Benutzerdefiniert“ und beende Elite.", true, null);
            yield break;
        }
        if (gpu?.VramGb is not { } vram) {
            yield return new("Grafikstufe", options.CurrentPreset?.Name ?? "eigene Werte", "–", "Der Grafikspeicher ist nicht bekannt.", true, null);
            yield break;
        }

        // Assumption by memory; Quality was measured on an RTX 4070 Ti (12 GB): station about 71 %, ring up to 94 %.
        var preset = GraphicsOptions.Presets.First(p => p.Name == (vram < 8 ? "Performance" : vram < 12 ? "Standard" : "Quality"));
        var fits = options.Matches(preset);
        var eliteNote = options.ActivePreset is { } active && active != "Custom"
            ? $" Elite nutzt gerade das Preset „{active}“, die Werte wirken vermutlich erst mit „Benutzerdefiniert“." : "";
        yield return new("Grafikstufe", options.CurrentPreset?.Name ?? "eigene Werte", preset.Name,
            $"{vram:0} GB Grafikspeicher. Annahme nach Speicher: unter 8 GB Performance, bis 11 GB Standard, ab 12 GB Quality " +
            "(gemessen auf einer RTX 4070 Ti mit 12 GB)." + eliteNote,
            fits, fits ? null : () => options.Write(GraphicsOptions.Values(preset)));

        var planets = GraphicsOptions.All.First(o => o.Key == "Planets/Ultra/TextureSize");
        var current = GraphicsOptions.ChoiceIndex(planets, options.Read(planets)) == 1 ? "8192" : "4096";
        var target = vram >= 16 ? "8192" : "4096";
        yield return new("Planetentexturen", current, target,
            vram >= 16 ? $"{vram:0} GB reichen für 8192, schärfere Planeten. Annahme, nicht gemessen."
                : $"Bei {vram:0} GB kann 8192 knapp werden, dann ruckelt es.",
            current == target, current == target ? null : () => options.Write([(planets, target)]));
    }

    // --- Hints from the Elite check ---------------------------------------------------------------------------------

    IEnumerable<Advice> CheckHints()
    {
        List<CheckResult> results;
        try {
            results = new EliteCheck(game, GraphicsFolder).Run();
        }
        catch (Exception) {
            yield break;
        }
        foreach (var r in results.Where(r => r.Status == CheckStatus.Warning && r.Group is EliteCheck.GroupGraphics or EliteCheck.GroupLimiter)) {
            yield return new(r.Title, "Warnung", "siehe „Elite-Check“", r.Detail, false, null);
        }
    }

    static int? Whole(string? text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    static double? Number(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
}
