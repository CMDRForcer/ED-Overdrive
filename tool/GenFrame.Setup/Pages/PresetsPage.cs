using System.Windows;
using System.Windows.Controls;
using GenFrame.Setup.Services;

namespace GenFrame.Setup.Pages;

static class PresetsPage
{
    static readonly (uint Value, string Text)[] FgChoices = [
        (NvDrs.Recommended, "Von NVIDIA empfohlen"),
        (1, "A (Standard)"), (2, "B (HUD-Fokus)"), (3, "C (Leistungsfokus)"), (4, "D"),
    ];

    public static UIElement Build(MainWindow window)
    {
        var page = new StackPanel();
        page.Children.Add(Ui.Heading("Presets"));
        page.Children.Add(Ui.Muted("Das DLAA-Modell fordert GEN·FRAME selbst an. Das FG-Preset legt der Treiber fest, " +
            "deshalb schreibt die App es ins Treiberprofil von Elite, nur auf Knopfdruck und mit Administrator-Abfrage. Andere Profile bleiben unberührt."));

        if (window.Game is { } game) {
            var model = AddonSettings.Get(game, "dlss_preset") ?? "0";
            var name = AddonSettings.DlaaModels.FirstOrDefault(c => c.Value == model).Text ?? $"Wert {model}";
            page.Children.Add(Ui.Row("DLAA-Modell", Ui.Text($"{name}, änderbar unter „Einstellungen“", Ui.Brush("MutedBrush"))));

            if (EliteCheck.ActiveFgPreset(game.Folder) is { } active) {
                var text = $"{active.Letter}, {(active.Applied ? "angewendet" : "nicht angewendet")}, {(active.Latest ? "das neueste" : "nicht das neueste")}";
                page.Children.Add(Ui.Row("FG-Preset in der letzten Sitzung (sl.log)", Ui.Text(text, Ui.Brush(active.Applied ? "OkBrush" : "WarnBrush"))));
            }
        }

        NvDrs.ProfileState state;
        try {
            state = NvDrs.Read();
        }
        catch (Exception e) {
            page.Children.Add(Ui.Row("NVIDIA-Treiber", Ui.Text("nicht lesbar, Presets gibt es nur mit NVIDIA-Karte", Ui.Brush("WarnBrush"))));
            page.Children.Add(Ui.Muted(e.Message));
            return page;
        }
        if (!state.ProfileFound) {
            page.Children.Add(Ui.Row("Treiberprofil", Ui.Text("der Treiber hat kein Profil für Elite Dangerous", Ui.Brush("WarnBrush"))));
            return page;
        }

        var own = state.Find(NvDrs.FgPreset)?.Own;
        var overrideOn = state.Find(NvDrs.FgOverride)?.Own == 1;
        var memory = NvDrs.LoadMemory();
        page.Children.Add(Ui.Row("FG-Preset im Treiberprofil",
            Ui.Text(own is { } value && overrideOn ? NvDrs.Letter(value) : "kein Override gesetzt", Ui.Brush("MutedBrush"))));

        var choices = FgChoices.ToList();
        if (own is { } set && choices.All(c => c.Value != set)) {
            choices.Add((set, NvDrs.Letter(set)));
        }
        var box = new ComboBox { Width = 200, Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var choice in choices) {
            box.Items.Add(choice.Text);
        }
        box.SelectedIndex = Math.Max(0, choices.FindIndex(c => c.Value == (memory?.Wanted ?? own)));
        page.Children.Add(box);

        // The NVIDIA App or a driver update can reset the profile.
        if (memory?.Wanted is { } wanted && (own != wanted || !overrideOn)) {
            page.Children.Add(Ui.Row($"Gewählt war {NvDrs.Letter(wanted)}, im Treiber steht etwas anderes",
                Ui.Text("„Anwenden“ setzt es erneut", Ui.Brush("WarnBrush"))));
        }

        var buttons = new List<UIElement> {
            Ui.Button("Anwenden", (_, _) => Run(window, () => NvDrs.SetFgPreset(choices[box.SelectedIndex].Value)), primary: true),
        };
        if (memory != null) {
            buttons.Add(Ui.Button("Vorherigen Stand wiederherstellen", (_, _) => Run(window, NvDrs.Restore)));
        }
        page.Children.Add(Ui.Buttons([.. buttons]));
        page.Children.Add(Ui.Muted("Die NVIDIA App kann diesen Wert überschreiben. Ob ein Preset wirklich aktiv war, zeigt die Zeile aus sl.log nach dem nächsten Spielstart."));
        return page;
    }

    static void Run(MainWindow window, Action change)
    {
        try {
            if (GameState.IsRunning()) {
                throw new InvalidOperationException("Elite läuft. Bitte das Spiel beenden und erneut versuchen.");
            }
            change();
            window.Refresh();
        }
        catch (OperationCanceledException e) {
            MessageBox.Show(window, e.Message, "GEN·FRAME Einrichtung");
        }
        catch (Exception e) {
            Ui.Error(e);
        }
    }
}
