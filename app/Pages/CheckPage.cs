using System.Windows;
using System.Windows.Controls;
using GenFrame.Setup.Services;

namespace GenFrame.Setup.Pages;

static class CheckPage
{
    public static UIElement Build(MainWindow window)
    {
        var page = new StackPanel();
        page.Children.Add(Ui.Heading("Elite-Check"));

        if (window.Game is not { } game) {
            page.Children.Add(Ui.Muted("Zuerst unter „Übersicht“ den Spielordner festlegen."));
            return page;
        }

        var results = new EliteCheck(game, EliteCheck.DefaultGraphicsFolder).Run();
        var warnings = results.Count(r => r.Status == CheckStatus.Warning);
        page.Children.Add(Ui.Muted(warnings == 0
            ? "Keine Probleme gefunden. Die Prüfung ändert nichts."
            : $"{warnings} {(warnings == 1 ? "Punkt braucht" : "Punkte brauchen")} Aufmerksamkeit. Die Prüfung ändert nichts, " +
              "erst der Knopf in der Zeile. Davor legt die App ein Backup an, Elite muss dafür geschlossen sein."));

        foreach (var group in results.GroupBy(r => r.Group)) {
            var title = Ui.Text(group.Key, Ui.Brush("AccentBrush"));
            title.Margin = new Thickness(0, 12, 0, 2);
            page.Children.Add(title);
            foreach (var result in group) {
                page.Children.Add(Row(window, game, result));
            }
        }

        page.Children.Add(Ui.Buttons(Ui.Button("Erneut prüfen", (_, _) => window.Refresh())));
        return page;
    }

    static Border Row(MainWindow window, GameInstall game, CheckResult result)
    {
        var left = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        left.Children.Add(Ui.Text(result.Title));
        var detail = Ui.Muted(result.Detail, 0);
        detail.TextWrapping = TextWrapping.Wrap;
        left.Children.Add(detail);

        UIElement right;
        if (result.Fix is { } fix) {
            right = Ui.Button(result.FixLabel ?? "Korrigieren", (_, _) => Apply(window, game, fix));
        }
        else {
            right = result.Status switch {
                CheckStatus.Ok => Ui.Text("passt", Ui.Brush("OkBrush")),
                CheckStatus.Warning => Ui.Text("prüfen", Ui.Brush("WarnBrush")),
                _ => Ui.Text("Hinweis", Ui.Brush("MutedBrush")),
            };
        }
        ((FrameworkElement)right).VerticalAlignment = VerticalAlignment.Center;
        return Ui.Row(left, right);
    }

    static void Apply(MainWindow window, GameInstall game, Action fix)
    {
        try {
            if (GameState.IsRunning()) {
                throw new InvalidOperationException("Elite läuft. Bitte das Spiel beenden und erneut versuchen.");
            }
            BackupService.Create("Vor Elite-Check-Korrektur", BackupsPage.FilesToSave(game));
            fix();
            window.Refresh();
        }
        catch (Exception e) {
            Ui.Error(e);
        }
    }
}
