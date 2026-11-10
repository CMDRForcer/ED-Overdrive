using System.Windows;
using System.Windows.Controls;
using GenFrame.Setup.Services;

namespace GenFrame.Setup.Pages;

static class GraphicsPage
{
    public static UIElement Build(MainWindow window)
    {
        var page = new StackPanel();
        page.Children.Add(Ui.Heading("Grafik"));

        if (window.Game is not { } game) {
            page.Children.Add(Ui.Muted("Zuerst unter „Übersicht“ den Spielordner festlegen."));
            return page;
        }

        var options = new GraphicsOptions(EliteCheck.DefaultGraphicsFolder);
        if (!options.Available) {
            page.Children.Add(Ui.Muted("Custom.4.4.fxcfg fehlt. Stelle die Grafik im Spiel einmal auf „Benutzerdefiniert“ und beende Elite."));
            return page;
        }

        page.Children.Add(Ui.Muted("Werte über das Grafikmenü von Elite hinaus. Sie kosten Bilder pro Sekunde, und die Kosten sind nicht gemessen. " +
            "Gilt ab dem nächsten Spielstart. Geändert wird erst beim Speichern, bei geschlossenem Elite und mit Backup vorher."));

        if (options.ActivePreset is { } preset && preset != "Custom") {
            page.Children.Add(Ui.Row($"Elite nutzt das Grafik-Preset „{preset}“",
                Ui.Text("Terrain, Triebwerke, Volumetrie und Sichtweite wirken vermutlich nur mit „Benutzerdefiniert“", Ui.Brush("WarnBrush"))));
        }

        var current = options.CurrentPreset;
        var presetTitle = Ui.Text("Voreinstellung", Ui.Brush("AccentBrush"));
        presetTitle.Margin = new Thickness(0, 12, 0, 0);
        page.Children.Add(presetTitle);
        page.Children.Add(Ui.Buttons(GraphicsOptions.Presets
            .Select(p => (UIElement)Ui.Button(p.Name, (_, _) => Save(window, game, options, GraphicsOptions.Values(p)), primary: p == current))
            .ToArray()));
        var setupLevel = new[] { GraphicsOptions.SupersamplingLevel, GraphicsOptions.Dlss5Level }.FirstOrDefault(options.Matches);
        page.Children.Add(Ui.Muted(current != null ? current.Hint
            : setupLevel != null ? $"Aktuell: {setupLevel.Hint} Gewechselt wird unter „Schnellwahl“."
            : "Aktuell: eigene Werte, keine der vier Stufen."));
        page.Children.Add(Ui.Muted("Ein Klick speichert sofort, mit Backup. Kantenglättung, Supersampling, Sichtfeld und Bewegungsunschärfe bleiben, wie sie sind."));

        var changes = new Dictionary<GraphicsOption, string>();
        foreach (var tier in GraphicsOptions.All.GroupBy(o => o.Tier)) {
            var title = Ui.Text(tier.Key == 1 ? "Stufe 1" : "Stufe 2: über das Menü hinaus, noch nicht getestet", Ui.Brush("AccentBrush"));
            title.Margin = new Thickness(0, 12, 0, 2);
            page.Children.Add(title);
            foreach (var option in tier) {
                page.Children.Add(Row(options, option, changes));
            }
        }

        page.Children.Add(Ui.Buttons(
            Ui.Button("Speichern", (_, _) => Save(window, game, options, changes.Select(c => (c.Key, c.Value))), primary: true),
            Ui.Button("Verwerfen", (_, _) => window.Refresh())));
        page.Children.Add(Ui.Muted("Stellst du im Spiel ein fertiges Grafik-Preset ein, kann Elite die Werte überschreiben. Die Seite zeigt immer den Stand aus den Dateien."));
        return page;
    }

    static void Save(MainWindow window, GameInstall game, GraphicsOptions options, IEnumerable<(GraphicsOption, string)> values)
    {
        try {
            if (GameState.IsRunning()) {
                throw new InvalidOperationException("Elite läuft. Bitte das Spiel beenden und erneut versuchen.");
            }
            var list = values.ToList();
            if (list.Count > 0) {
                BackupService.Create("Vor Grafik-Änderung", BackupsPage.FilesToSave(game));
                options.Write(list);
            }
            window.Refresh();
        }
        catch (Exception e) {
            Ui.Error(e);
        }
    }

    static Border Row(GraphicsOptions options, GraphicsOption option, Dictionary<GraphicsOption, string> changes)
    {
        var left = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        left.Children.Add(Ui.Text(option.Label));
        var hint = Ui.Muted(option.Hint, 0);
        hint.TextWrapping = TextWrapping.Wrap;
        left.Children.Add(hint);

        var index = GraphicsOptions.ChoiceIndex(option, options.Read(option));
        FrameworkElement control;
        if (option.Choices.Length == 2) {
            var box = new CheckBox { IsChecked = index == 1, VerticalAlignment = VerticalAlignment.Center };
            box.Click += (_, _) => changes[option] = option.Choices[box.IsChecked == true ? 1 : 0].Value;
            control = box;
        }
        else {
            var box = new ComboBox { Width = 170, VerticalAlignment = VerticalAlignment.Center };
            foreach (var choice in option.Choices) {
                box.Items.Add(choice.Text);
            }
            box.SelectedIndex = index;
            box.SelectionChanged += (_, _) => changes[option] = option.Choices[box.SelectedIndex].Value;
            control = box;
        }
        return Ui.Row(left, control);
    }
}
