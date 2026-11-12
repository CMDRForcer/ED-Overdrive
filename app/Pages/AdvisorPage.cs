using System.IO;
using System.Windows;
using System.Windows.Controls;
using GenFrame.Setup.Services;

namespace GenFrame.Setup.Pages;

// Suggestions for this PC (Advisor). Only reads until a button is clicked; every change needs Elite closed and makes a backup.
static class AdvisorPage
{
    public static UIElement Build(MainWindow window)
    {
        var page = new StackPanel();
        page.Children.Add(Ui.Heading("Berater"));

        if (window.Game is not { } game) {
            page.Children.Add(Ui.Muted("Zuerst unter „Übersicht“ den Spielordner festlegen."));
            return page;
        }
        if (!File.Exists(Path.Combine(game.Folder, "ReShade.ini"))) {
            page.Children.Add(Ui.Muted("EDAA ist noch nicht eingerichtet. Bitte zuerst unter „Übersicht“ installieren."));
            return page;
        }

        page.Children.Add(Ui.Muted("Vorschläge für diesen PC aus Grafikkarte, Bildschirm und den Einstellungen von Elite. " +
            "Geändert wird nur auf Klick, bei geschlossenem Elite und mit Backup vorher. Gemessen ist bisher nur auf einer RTX 4070 Ti in 1440p mit 144 Hz, " +
            "alles andere ist als Annahme markiert."));

        var facts = Advisor.Facts();
        var system = Ui.Text("Dieser PC", Ui.Brush("AccentBrush"));
        system.Margin = new Thickness(0, 4, 0, 2);
        page.Children.Add(system);
        page.Children.Add(Ui.Row("Grafikkarte", Ui.Text(facts.Gpu is { } gpu
            ? gpu.Name + (gpu.VramGb is { } vram ? $", {vram:0} GB" : ", Speicher unbekannt") : "nicht erkannt", Ui.Brush("MutedBrush"))));
        page.Children.Add(Ui.Row("Auflösung im Spiel", Ui.Text(facts.Width is { } w && facts.Height is { } h ? $"{w} × {h}" : "unbekannt", Ui.Brush("MutedBrush"))));
        page.Children.Add(Ui.Row("Bildwiederholrate", Ui.Text(facts.RefreshRate is { } hz ? $"{hz} Hz" : "unbekannt", Ui.Brush("MutedBrush"))));
        page.Children.Add(Ui.Row("Windows-HDR", Ui.Text(facts.Hdr switch { true => "an", false => "aus", _ => "unbekannt" }, Ui.Brush("MutedBrush"))));

        List<Advice> advice;
        try {
            advice = new Advisor(game).Suggest(facts);
        }
        catch (Exception e) {
            page.Children.Add(Ui.Text("Der Berater konnte die Einstellungen nicht lesen: " + e.Message, Ui.Brush("WarnBrush")));
            return page;
        }

        var title = Ui.Text("Vorschläge", Ui.Brush("AccentBrush"));
        title.Margin = new Thickness(0, 16, 0, 2);
        page.Children.Add(title);
        foreach (var a in advice) {
            page.Children.Add(Row(window, game, a));
        }

        var open = advice.Where(a => a.Apply != null).ToList();
        if (open.Count == 0) {
            page.Children.Add(Ui.Muted("Alles passt zu den Vorschlägen.", 0));
        }
        else {
            page.Children.Add(Ui.Buttons(Ui.Button($"Alle übernehmen ({open.Count})", (_, _) => Apply(window, game, open), primary: true)));
        }
        return page;
    }

    static Border Row(MainWindow window, GameInstall game, Advice a)
    {
        var left = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        left.Children.Add(Ui.Text(a.Title));
        var values = Ui.Text($"Jetzt: {a.Current}  ·  Empfohlen: {a.Recommended}", Ui.Brush(a.Fits ? "OkBrush" : "WarnBrush"));
        left.Children.Add(values);
        var why = Ui.Muted(a.Why, 0);
        why.TextWrapping = TextWrapping.Wrap;
        left.Children.Add(why);

        UIElement right = a.Apply != null
            ? Ui.Button("Übernehmen", (_, _) => Apply(window, game, [a]))
            : Ui.Text(a.Fits ? "passt" : "", Ui.Brush("OkBrush"));
        if (right is FrameworkElement element) {
            element.VerticalAlignment = VerticalAlignment.Center;
        }
        return Ui.Row(left, right);
    }

    static void Apply(MainWindow window, GameInstall game, List<Advice> list)
    {
        try {
            if (GameState.IsRunning()) {
                throw new InvalidOperationException("Elite läuft. Bitte das Spiel beenden und erneut versuchen.");
            }
            BackupService.Create("Vor Berater", BackupsPage.FilesToSave(game));
            foreach (var a in list) {
                a.Apply!();
            }
        }
        catch (Exception e) {
            Ui.Error(e);
        }
        window.Refresh();
    }
}
