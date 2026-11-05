using System.IO;
using System.Windows;
using System.Windows.Controls;
using GenFrame.Setup.Services;

namespace GenFrame.Setup.Pages;

static class BackupsPage
{
    public static UIElement Build(MainWindow window)
    {
        var page = new StackPanel();
        page.Children.Add(Ui.Heading("Backups"));
        page.Children.Add(Ui.Muted($"Gespeichert unter {BackupService.Root}. Wiederherstellen sichert vorher den aktuellen Stand."));

        var backups = BackupService.List();
        if (backups.Count == 0) {
            page.Children.Add(Ui.Muted("Noch keine Backups."));
        }
        foreach (var backup in backups) {
            var files = string.Join(", ", backup.Files.Select(f => Path.GetFileName(f.Original)));
            var restore = Ui.Button("Wiederherstellen", (_, _) => Restore(window, backup));
            restore.Margin = new Thickness(0);
            page.Children.Add(Ui.Row($"{backup.Created:dd.MM.yyyy HH:mm} · {backup.Label}\n{files}", restore));
        }

        if (window.Game is { } game) {
            page.Children.Add(Ui.Buttons(Ui.Button("Jetzt sichern", (_, _) => {
                try {
                    BackupService.Create("Manuell", FilesToSave(game));
                    window.Refresh();
                }
                catch (Exception e) {
                    Ui.Error(e);
                }
            }, primary: true)));
        }
        return page;
    }

    // Everything the tool may change later: ReShade, its preset and Elite's graphics options.
    public static IEnumerable<string> FilesToSave(GameInstall game)
    {
        yield return Path.Combine(game.Folder, "ReShade.ini");
        foreach (var preset in Directory.EnumerateFiles(game.Folder, "*Preset*.ini")) {
            yield return preset;
        }

        var graphics = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Frontier Developments", "Elite Dangerous", "Options", "Graphics");
        if (Directory.Exists(graphics)) {
            foreach (var name in new[] { "Settings.xml", "DisplaySettings.xml", "GraphicsConfigurationOverride.xml" }) {
                yield return Path.Combine(graphics, name);
            }
            foreach (var fxcfg in Directory.EnumerateFiles(graphics, "*.fxcfg")) {
                yield return fxcfg;
            }
        }
    }

    static void Restore(MainWindow window, Backup backup)
    {
        var answer = MessageBox.Show(window, $"Backup „{backup.Label}“ vom {backup.Created:dd.MM.yyyy HH:mm} wiederherstellen?",
            "GEN·FRAME Einrichtung", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) {
            return;
        }
        try {
            BackupService.Restore(backup);
            window.Refresh();
        }
        catch (Exception e) {
            Ui.Error(e);
        }
    }
}
