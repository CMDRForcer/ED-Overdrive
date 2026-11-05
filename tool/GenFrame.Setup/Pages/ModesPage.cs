using System.IO;
using System.Windows;
using System.Windows.Controls;
using GenFrame.Setup.Services;

namespace GenFrame.Setup.Pages;

static class ModesPage
{
    public static UIElement Build(MainWindow window)
    {
        var page = new StackPanel();
        page.Children.Add(Ui.Heading("Schnellwahl"));

        if (window.Game is not { } game) {
            page.Children.Add(Ui.Muted("Zuerst unter „Übersicht“ den Spielordner festlegen."));
            return page;
        }
        if (!File.Exists(Path.Combine(game.Folder, "ReShade.ini"))) {
            page.Children.Add(Ui.Muted("EDAA ist noch nicht eingerichtet. Bitte zuerst unter „Übersicht“ installieren."));
            return page;
        }

        page.Children.Add(Ui.Muted("Ein Klick setzt alle nötigen Werte, Filter und Feineinstellungen bleiben. " +
            "Gilt ab dem nächsten Spielstart, Elite muss dafür geschlossen sein."));

        var current = AddonSettings.CurrentMode(game);
        var cards = new WrapPanel();
        cards.Children.Add(Card(window, game, QuickMode.DlaaFg, "DLAA + FG", "Beste Bildqualität mit verdoppelter Bildrate. RTX 40/50.", current));
        cards.Children.Add(Card(window, game, QuickMode.Dlaa, "Nur DLAA", "Kantenglättung von NVIDIA, ohne generierte Bilder.", current));
        cards.Children.Add(Card(window, game, QuickMode.TaaFg, "Nur FG", "Kantenglättung mit TAA statt DLAA. Noch nicht im Spiel geprüft.", current));
        cards.Children.Add(Card(window, game, QuickMode.Off, "Aus", "Keine Kantenglättung und kein FG. Die ReShade-Filter bleiben.", current));
        page.Children.Add(cards);

        if (current == null) {
            page.Children.Add(Ui.Muted("Die aktuellen Werte passen zu keiner der vier Varianten, sie wurden im Overlay oder von Hand geändert."));
        }

        var renodx = InstallInfo.Scan(game.Folder).Any(c => c.Name == "RenoDX" && c.State == ComponentState.Installed);
        if (renodx) {
            page.Children.Add(Ui.Row("RenoDX ist an", Ui.Text("mit FG schwarzes Bild, im Overlay unter „Add-ons“ abschalten", Ui.Brush("WarnBrush"))));
        }
        return page;
    }

    static Border Card(MainWindow window, GameInstall game, QuickMode mode, string title, string text, QuickMode? current)
    {
        var content = new StackPanel();
        content.Children.Add(Ui.Text(title));
        content.Children.Add(Ui.Muted(text, 8));
        if (mode == current) {
            content.Children.Add(Ui.Text("aktiv", Ui.Brush("OkBrush")));
        }
        else {
            var use = Ui.Button("Verwenden", (_, _) => {
                try {
                    if (GameState.IsRunning()) {
                        throw new InvalidOperationException("Elite läuft. Bitte das Spiel beenden und erneut versuchen.");
                    }
                    BackupService.Create("Vor Schnellwahl", BackupsPage.FilesToSave(game));
                    AddonSettings.Write(game, AddonSettings.ModeValues(mode));
                    window.Refresh();
                }
                catch (Exception e) {
                    Ui.Error(e);
                }
            });
            use.HorizontalAlignment = HorizontalAlignment.Left;
            content.Children.Add(use);
        }
        var card = Ui.Card(content, 210);
        if (mode == current) {
            card.BorderBrush = Ui.Brush("AccentBrush");
        }
        return card;
    }
}
