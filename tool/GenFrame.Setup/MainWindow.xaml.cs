using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using GenFrame.Setup.Pages;
using GenFrame.Setup.Services;

namespace GenFrame.Setup;

public partial class MainWindow : Window
{
    readonly DispatcherTimer gameStateTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    string currentPage = "overview";

    public MainWindow()
    {
        InitializeComponent();
        UpdateGameState();
        gameStateTimer.Tick += (_, _) => UpdateGameState();
        gameStateTimer.Start();
        ShowPage(currentPage);
    }

    public GameInstall? Game { get; private set; } = AppSettings.LoadGame();

    public void SetGame(GameInstall game)
    {
        Game = game;
        AppSettings.SaveGame(game);
        ShowPage(currentPage);
    }

    public void Refresh() => ShowPage(currentPage);

    void OnNavigate(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string page } && PageHost != null) {
            ShowPage(page);
        }
    }

    void ShowPage(string page)
    {
        currentPage = page;
        PageHost.Content = page switch {
            "overview" => OverviewPage.Build(this),
            "backups" => BackupsPage.Build(this),
            "modes" => PlaceholderPage.Build("Schnellwahl", "DLAA + FG, nur DLAA, nur FG, aus – frei kombinierbar."),
            "filters" => FilterPage.Build(this),
            "check" => CheckPage.Build(this),
            "presets" => PlaceholderPage.Build("Presets", "DLAA- und FG-Presets im Treiberprofil von Elite wählen und aktive Presets aus sl.log anzeigen."),
            "settings" => PlaceholderPage.Build("Einstellungen", "GEN·FRAME-Einstellungen bei geschlossenem Spiel bearbeiten."),
            "diagnose" => PlaceholderPage.Build("Diagnose", "Logs, Dumps und Einstellungen als Zip sammeln."),
            _ => null,
        };
    }

    void UpdateGameState()
    {
        bool running = GameState.IsRunning();
        GameStateText.Text = running ? "Elite läuft – Änderungen gesperrt" : "Elite ist geschlossen";
        GameStateText.Foreground = (Brush)FindResource(running ? "WarnBrush" : "OkBrush");
        GameStateBadge.Background = (Brush)FindResource("CardBrush");
    }
}
