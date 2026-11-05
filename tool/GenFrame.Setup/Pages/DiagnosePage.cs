using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using GenFrame.Setup.Services;

namespace GenFrame.Setup.Pages;

static class DiagnosePage
{
    public static UIElement Build(MainWindow window)
    {
        var page = new StackPanel();
        page.Children.Add(Ui.Heading("Diagnose"));

        if (window.Game is not { } game) {
            page.Children.Add(Ui.Muted("Zuerst unter „Übersicht“ den Spielordner festlegen."));
            return page;
        }

        page.Children.Add(Ui.Muted("Packt Logs, Einstellungen und einen Bericht mit dem Elite-Check in ein Zip, das du bei einem Problem weitergeben kannst. " +
            "Hochgeladen wird nichts. Die Journale bleiben draußen, weil darin dein Kommandantenname und deine Position stehen."));

        var service = new DiagnoseService(game, EliteCheck.DefaultGraphicsFolder, _ => { });
        page.Children.Add(Ui.Row("Dateien im Zip", Ui.Text($"{service.TextFiles().Count} und der Bericht", Ui.Brush("MutedBrush"))));

        var includeDump = new CheckBox { VerticalAlignment = VerticalAlignment.Center };
        if (DiagnoseService.NewestDump() is { } dump) {
            var left = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
            left.Children.Add(Ui.Text($"Neuesten Absturz-Dump anhängen ({dump.LastWriteTime:dd.MM.yyyy}, {dump.Length / 1048576} MB)"));
            var warning = Ui.Muted("Ein Dump ist ein Abbild des Spielspeichers und kann persönliche Daten enthalten. Nur an Personen geben, denen du vertraust.", 0);
            warning.TextWrapping = TextWrapping.Wrap;
            left.Children.Add(warning);
            page.Children.Add(Ui.Row(left, includeDump));
        }

        page.Children.Add(Ui.Buttons(Ui.Button("Zip erstellen", (_, _) => Create(window, game, includeDump.IsChecked == true), primary: true)));
        return page;
    }

    static void Create(MainWindow window, GameInstall game, bool withDump)
    {
        var dialog = new SaveFileDialog {
            Title = "Diagnose-Zip speichern",
            FileName = $"GenFrame-Diagnose-{DateTime.Now:yyyyMMdd-HHmm}.zip",
            Filter = "Zip-Datei|*.zip",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };
        if (dialog.ShowDialog(window) != true) {
            return;
        }

        var target = dialog.FileName;
        InstallDialog.RunTask(window, "Diagnose", async log => {
            if (File.Exists(target)) {
                File.Delete(target); // the save dialog already asked
            }
            await Task.Run(() => new DiagnoseService(game, EliteCheck.DefaultGraphicsFolder, log).Create(target, withDump));
            log($"Fertig: {target}");
            Process.Start("explorer.exe", $"/select,\"{target}\"")?.Dispose();
        });
    }
}
