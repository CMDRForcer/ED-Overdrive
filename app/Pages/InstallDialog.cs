using System.IO;
using System.Windows;
using System.Windows.Controls;
using GenFrame.Setup.Services;

namespace GenFrame.Setup.Pages;

// Shows what will happen, asks once, then runs the installation with a live log.
sealed class InstallDialog : Window
{
    readonly TextBox logBox = new() {
        IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        Background = Ui.Brush("CardBrush"), Foreground = Ui.Brush("TextBrush"), BorderBrush = Ui.Brush("BorderBrush"),
        FontFamily = new System.Windows.Media.FontFamily("Consolas"), Padding = new Thickness(8),
    };
    readonly Button closeButton;

    InstallDialog(Window owner, string title)
    {
        Owner = owner;
        Title = title;
        Width = 640;
        Height = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Ui.Brush("WindowBrush");

        closeButton = Ui.Button("Schließen", (_, _) => Close());
        closeButton.IsEnabled = false;
        closeButton.HorizontalAlignment = HorizontalAlignment.Right;
        closeButton.Margin = new Thickness(0, 10, 0, 0);

        var panel = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(closeButton, Dock.Bottom);
        panel.Children.Add(closeButton);
        panel.Children.Add(logBox);
        Content = panel;
    }

    void Log(string line) => Dispatcher.Invoke(() => {
        logBox.AppendText($"{DateTime.Now:HH:mm:ss}  {line}\n");
        logBox.ScrollToEnd();
    });

    // Runs any installation step with the same log window (used by the filter page too).
    public static void RunTask(MainWindow owner, string title, Func<Action<string>, Task> work)
    {
        var dialog = new InstallDialog(owner, title);
        dialog.Loaded += async (_, _) => {
            try {
                await Task.Run(() => work(dialog.Log));
            }
            catch (Exception e) {
                dialog.Log("FEHLER: " + e.Message);
            }
            finally {
                dialog.closeButton.IsEnabled = true;
            }
        };
        dialog.ShowDialog();
        owner.Refresh();
    }

    public static void Run(MainWindow owner, GameInstall game, Channel channel, bool clean)
    {
        var question = clean
            ? "Sauber neu installieren:\n\n" +
              $"• {CleanupService.FindOldFiles(game).Count} Dateien/Ordner von ReShade, EDAA, Streamline und Test-Builds " +
              $"werden nach {CleanupService.Root} verschoben (nicht gelöscht).\n" +
              "• EDHM, RenoDX und die Spieldateien bleiben unverändert.\n" +
              "• Danach werden ReShade, EDAA und Streamline frisch installiert, mit Standardwerten.\n" +
              "• Das Filter-Set wird mit der Variante „Lebendig“ eingerichtet (umschaltbar unter „Filter“).\n\nFortfahren?"
            : "Installieren / Aktualisieren:\n\n" +
              "• ReShade, EDAA und Streamline werden installiert oder ersetzt.\n" +
              "• Deine Einstellungen in ReShade.ini bleiben, fehlende Werte werden ergänzt.\n" +
              "• Vorher wird ein Backup angelegt.\n\nFortfahren?";
        if (MessageBox.Show(owner, question, "ED-Overdrive", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) {
            return;
        }

        var dialog = new InstallDialog(owner, clean ? "Saubere Neuinstallation" : "Installation");
        dialog.Loaded += async (_, _) => {
            try {
                dialog.Log($"Spielordner: {game.Folder}");
                dialog.Log($"Kanal: {(channel == Channel.Stable ? $"Stabil (EDAA {InstallService.BundledEdaa}, in der App)" : "Entwicklung (neuester Build)")}");
                await Task.Run(() => new InstallService(game, dialog.Log).Install(channel, clean));
            }
            catch (Exception e) {
                dialog.Log("FEHLER: " + e.Message);
                dialog.Log("Es wurde abgebrochen. Ein Backup des vorherigen Stands liegt unter „Backups“.");
            }
            finally {
                dialog.closeButton.IsEnabled = true;
            }
        };
        dialog.ShowDialog();
        owner.Refresh();
    }
}
