using System.IO;
using System.Text.Json;

namespace GenFrame.Setup.Services;

// The tool's own settings (chosen game folder, install channel), kept next to the backups.
public static class AppSettings
{
    sealed record Data(string? GameFolder, GameStore? Store, Channel? Channel);

    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GenFrame", "settings.json");

    static Data Load()
    {
        try {
            if (File.Exists(FilePath)) {
                return JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath)) ?? new Data(null, null, null);
            }
        }
        catch (JsonException) {
            // Fall back to defaults.
        }
        return new Data(null, null, null);
    }

    static void Save(Data data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(data));
    }

    public static GameInstall? LoadGame()
    {
        var data = Load();
        if (data.GameFolder != null && GameLocator.IsGameFolder(data.GameFolder)) {
            return new GameInstall(data.GameFolder, data.Store ?? GameStore.Manual);
        }
        return GameLocator.FindAll().FirstOrDefault();
    }

    public static void SaveGame(GameInstall game) => Save(Load() with { GameFolder = game.Folder, Store = game.Store });

    // Development until a release contains the Steam overlay fix (1a8f6a2); the newest release
    // (v0.5.2-cmdr1) crashes in gameoverlayrenderer64.dll.
    public static Channel LoadChannel() => Load().Channel ?? Channel.Development;

    public static void SaveChannel(Channel channel) => Save(Load() with { Channel = channel });
}
