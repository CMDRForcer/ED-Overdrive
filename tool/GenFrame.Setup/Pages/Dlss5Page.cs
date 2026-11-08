using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using GenFrame.Setup.Services;

namespace GenFrame.Setup.Pages;

// DLSS 5 through GITC Uplift (UpliftService). Every change only with Elite closed and a backup first.
static class Dlss5Page
{
    public static UIElement Build(MainWindow window)
    {
        var page = new StackPanel();
        page.Children.Add(Ui.Heading("DLSS 5"));
        page.Children.Add(Ui.Muted("NVIDIAs neuronales Rendering verbessert Licht, Materialien und Farben im fertigen Bild. " +
            $"Die App bringt GITC Uplift {UpliftService.Version} mit (MIT, ghostinthecamera). Uplift nutzt den DLSS-Aufruf von EDAA " +
            "und dessen Bewegungsdaten. Optimiert gemessen auf einer RTX 4070 Ti in 1440p: etwa 67 echte Bilder pro Sekunde " +
            "(134 mit Frame Generation), DLSS 5 belegt rund 0,4 GB Grafikspeicher."));

        if (window.Game is not { } game) {
            page.Children.Add(Ui.Muted("Zuerst unter „Übersicht“ den Spielordner festlegen."));
            return page;
        }

        var state = UpliftService.Read(game);
        var de = CultureInfo.GetCultureInfo("de-DE");

        page.Children.Add(Ui.Row("GITC Uplift", state.Installed
            ? Ui.Text(state.CurrentVersion ? $"{UpliftService.Version} installiert" : "installiert, andere Version", Ui.Brush("OkBrush"))
            : Ui.Text("nicht installiert", Ui.Brush("MutedBrush"))));

        if (state.RuntimeHash != null) {
            page.Children.Add(Ui.Row($"{UpliftService.RuntimeName} im Spiel", RuntimeText(state.RuntimeHash)));
        }
        var stored = File.Exists(UpliftService.StoredRuntime);
        page.Children.Add(Ui.Row($"{UpliftService.RuntimeName} in der App gemerkt",
            stored ? RuntimeText(Sha256Short(UpliftService.StoredRuntime)) : Ui.Text("noch keine gewählt", Ui.Brush("WarnBrush"))));

        if (state.Installed) {
            page.Children.Add(Ui.Row("DLSS 5", Ui.Text(state.Enabled ? "an" : "aus", Ui.Brush(state.Enabled ? "OkBrush" : "MutedBrush"))));
            var tuned = state.EdaaDlss && state.BeforeUpscaling && !state.KeepFaces && state.FilterActive
                && state.Scale is { } s && Math.Abs(s - UpliftService.RenderScale) < 0.001;
            var scale = state.Scale?.ToString("0.00", de) ?? "?";
            page.Children.Add(Ui.Row($"Optimiert (EDAA DLSS, Auflösung {scale}, vor dem Hochskalieren, Filter „DLSS 5“)",
                Ui.Text(tuned ? "ja" : "nein", Ui.Brush(tuned ? "OkBrush" : "WarnBrush"))));
            if (new GraphicsOptions(EliteCheck.DefaultGraphicsFolder).CurrentPreset?.Name == "Quality+") {
                page.Children.Add(Ui.Row("Grafik „Quality+“", Ui.Text("mit DLSS 5 wird der Grafikspeicher knapp, besser „Quality“", Ui.Brush("WarnBrush"))));
            }
        }

        page.Children.Add(Ui.Muted(
            $"{UpliftService.RuntimeName} ist eine NVIDIA-Datei und darf nicht mitgeliefert werden. Auf RTX 50 läuft das NVIDIA-Original, " +
            "auf RTX 20/30/40 nur ein angepasster Community-Build (unsigniert, auf eigenes Risiko; ab 310.9.16 mit dem schnelleren Netz Small FP8). " +
            "Die App prüft die Datei anhand ihrer Prüfsumme.", 4));

        var buttons = new List<UIElement> {
            Ui.Button(stored ? "DLL wechseln" : "DLL auswählen", (_, _) => PickRuntime(window), primary: !stored),
        };
        if (stored) {
            buttons.Add(Ui.Button(state.Installed ? "Neu installieren" : "Installieren", (_, _) =>
                InstallDialog.RunTask(window, "DLSS 5", log => {
                    UpliftService.Install(game, log);
                    log("Fertig. Im Spiel: Pos1 → GITC Uplift.");
                    return Task.CompletedTask;
                }), primary: !state.Installed));
        }
        if (state.Installed) {
            buttons.Add(Ui.Button(state.Enabled ? "Ausschalten" : "Einschalten", (_, _) => Run(window, game,
                state.Enabled ? "Vor DLSS 5 aus" : "Vor DLSS 5 an", () => UpliftService.SetEnabled(game, !state.Enabled))));
            buttons.Add(Ui.Button("Optimieren", (_, _) =>
                InstallDialog.RunTask(window, "DLSS 5 optimieren", log => {
                    BackupService.Create("Vor DLSS-5-Optimierung", BackupsPage.FilesToSave(game));
                    log("Backup angelegt.");
                    UpliftService.Optimize(game, log);
                    return Task.CompletedTask;
                })));
            buttons.Add(Ui.Button("Entfernen", (_, _) => {
                if (MessageBox.Show(window, "DLSS 5 entfernen? Die Dateien werden nach „Entfernt“ verschoben, Auflösung und Filter gehen zurück.",
                        "ED-Overdrive", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) {
                    InstallDialog.RunTask(window, "DLSS 5 entfernen", log => {
                        UpliftService.Remove(game, log);
                        return Task.CompletedTask;
                    });
                }
            }));
        }
        page.Children.Add(Ui.Buttons([.. buttons]));
        page.Children.Add(Ui.Muted("Optimieren: EDAA auf DLSS, Elite rendert mit 0,65 und DLSS 5 läuft vor dem Hochskalieren (spart Rechenzeit), " +
            "Gesichtsmaske aus, Filter „DLSS 5“ statt „Lebendig“. Feinjustieren im Spiel unter Pos1 → GITC Uplift → Look.", 0));
        return page;
    }

    static TextBlock RuntimeText(string hash)
    {
        var known = UpliftService.Identify(hash);
        return known != null
            ? Ui.Text(known.Label, Ui.Brush(known.ForRtx40 ? "TextBrush" : "WarnBrush"))
            : Ui.Text($"unbekannter Build ({hash[..8]}…)", Ui.Brush("WarnBrush"));
    }

    static string Sha256Short(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream));
    }

    static void PickRuntime(MainWindow window)
    {
        var dialog = new OpenFileDialog { Filter = $"{UpliftService.RuntimeName}|{UpliftService.RuntimeName}", Title = "nvngx_dlssnr.dll wählen" };
        if (dialog.ShowDialog(window) != true) {
            return;
        }
        try {
            var known = UpliftService.StoreRuntime(dialog.FileName);
            var text = known == null
                ? "Unbekannter Build. Er wurde gemerkt, ob er auf deiner Karte läuft, zeigt erst das Spiel."
                : known.ForRtx40 ? $"Erkannt: {known.Label}." : $"Erkannt: {known.Label}. Auf RTX 20/30/40 erzeugt er das Modell nicht.";
            MessageBox.Show(window, text, "ED-Overdrive", MessageBoxButton.OK, known?.ForRtx40 == true ? MessageBoxImage.Information : MessageBoxImage.Warning);
            window.Refresh();
        }
        catch (Exception e) {
            Ui.Error(e);
        }
    }

    static void Run(MainWindow window, GameInstall game, string label, Action change)
    {
        try {
            if (GameState.IsRunning()) {
                throw new InvalidOperationException("Elite läuft. Bitte das Spiel beenden und erneut versuchen.");
            }
            BackupService.Create(label, BackupsPage.FilesToSave(game));
            change();
            window.Refresh();
        }
        catch (Exception e) {
            Ui.Error(e);
        }
    }
}
