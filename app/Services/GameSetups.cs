using System.Globalization;
using System.IO;

namespace GenFrame.Setup.Services;

public enum GameSetup { Supersampling, Dlss5 }

// Two complete ways to play, switched with one click: render scale, DLSS 5 on or off, EDAA's method,
// Elite's graphics values and the filter set belong together. Switching only DLSS 5 left 0.85 with
// DLSS 5 off or 1.5 with DLSS 5 on (10.11.2026). Filter fine-tuning and the other EDAA values stay.
public static class GameSetups
{
    // Measured 11.11.2026 on an RTX 4070 Ti in 1440p with DLAA (model K, 1.5 ms) and Ultra graphics: 70 real -> 140 FPS
    // (the frame limit) in space, asteroid field and hangar, about 65 real in a busy concourse. 1.5 needed lower graphics.
    public const double SupersamplingScale = 1.25;

    // Resolve before the HUD pass. Without it EDAA resolves at the bloom pass, and with the lowered bloom of the
    // supersampling graphics that pass was not found (10.11.2026: "resolve point not reached ... bloom no"):
    // no DLSS call, so DLSS 5 waited ("Waiting for DLSS").
    static readonly Dictionary<string, string> BeforeHud = new() { ["taa_before_hud"] = "1" };

    static string CustomXml => Path.Combine(EliteCheck.DefaultGraphicsFolder, "Custom.4.4.fxcfg");

    public static bool Dlss5Installed(GameInstall game) => UpliftService.Read(game).Installed;

    // Null when neither fits: render scale and DLSS 5 decide, graphics values may be tuned by hand.
    public static GameSetup? Current(GameInstall game)
    {
        var state = UpliftService.Read(game);
        if (state.Scale is not { } scale) {
            return null;
        }
        if (state.Enabled && Math.Abs(scale - UpliftService.RenderScale) < 0.001) {
            return GameSetup.Dlss5;
        }
        if (!state.Enabled && Math.Abs(scale - SupersamplingScale) < 0.001) {
            return GameSetup.Supersampling;
        }
        return null;
    }

    public static void Apply(GameInstall game, GameSetup setup, Action<string> log)
    {
        if (GameState.IsRunning()) {
            throw new InvalidOperationException("Elite läuft. Bitte das Spiel beenden und erneut versuchen.");
        }
        if (!File.Exists(CustomXml)) {
            throw new FileNotFoundException("Custom.4.4.fxcfg fehlt. Stelle die Grafik im Spiel einmal auf „Benutzerdefiniert“ und beende Elite.");
        }
        if (setup == GameSetup.Dlss5 && !Dlss5Installed(game)) {
            throw new InvalidOperationException("DLSS 5 ist nicht installiert. Bitte zuerst auf der Seite „DLSS 5“ installieren.");
        }

        BackupService.Create("Vor Spiel-Setup", Pages.BackupsPage.FilesToSave(game));
        log("Backup angelegt.");
        var graphics = new GraphicsOptions(EliteCheck.DefaultGraphicsFolder);

        if (setup == GameSetup.Dlss5) {
            // Render scale 0.85, EDAA DLSS (model K), Uplift on and tuned, filter "DLSS 5".
            UpliftService.Optimize(game, log);
            AddonSettings.Write(game, BeforeHud);
            graphics.Write(GraphicsOptions.Values(GraphicsOptions.Dlss5Level));
            log($"Grafik: „{GraphicsOptions.Dlss5Level.Name}“.");
            return;
        }

        if (Dlss5Installed(game)) {
            UpliftService.SetEnabled(game, false);
            log("DLSS 5 aus (bleibt installiert).");
        }
        // DLAA at the full supersampled size; the DLAA model the user picked stays.
        AddonSettings.Write(game, new Dictionary<string, string>(BeforeHud) { ["aa_method"] = "1" });
        EliteCheck.SetXml(CustomXml, "SSAAMultiplier", SupersamplingScale.ToString("0.000000", CultureInfo.InvariantCulture));
        log("Elite rendert mit 1,25, EDAA glättet mit DLAA.");
        graphics.Write(GraphicsOptions.Values(GraphicsOptions.SupersamplingLevel));
        log($"Grafik: „{GraphicsOptions.SupersamplingLevel.Name}“.");
        if (FilterService.Active(game) == FilterVariant.Dlss5) {
            new FilterService(game, log).SetActive(FilterVariant.Vivid);
            log("Filter: „Lebendig“.");
        }
    }
}
