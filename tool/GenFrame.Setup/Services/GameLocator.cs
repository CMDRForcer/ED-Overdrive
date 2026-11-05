using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace GenFrame.Setup.Services;

public enum GameStore { Steam, Epic, Frontier, Manual }

public sealed record GameInstall(string Folder, GameStore Store)
{
    public string Exe => Path.Combine(Folder, GameLocator.ExeName);
}

// Finds the Elite Dangerous Odyssey folder (the one with EliteDangerous64.exe).
public static partial class GameLocator
{
    public const string ExeName = "EliteDangerous64.exe";
    const string ProductsSubPath = @"Products\elite-dangerous-odyssey-64";

    public static List<GameInstall> FindAll()
    {
        var found = new List<GameInstall>();
        found.AddRange(FromSteam());
        found.AddRange(FromEpic());
        found.AddRange(FromFrontier());
        return found
            .Where(g => File.Exists(g.Exe))
            .DistinctBy(g => Path.GetFullPath(g.Folder).TrimEnd('\\').ToLowerInvariant())
            .ToList();
    }

    public static bool IsGameFolder(string folder) => File.Exists(Path.Combine(folder, ExeName));

    static IEnumerable<GameInstall> FromSteam()
    {
        var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (string.IsNullOrEmpty(steam)) {
            yield break;
        }

        var libraries = new List<string> { steam.Replace('/', '\\') };
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf)) {
            foreach (Match m in VdfPath().Matches(File.ReadAllText(vdf))) {
                libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
            }
        }

        foreach (var lib in libraries) {
            yield return new GameInstall(Path.Combine(lib, "steamapps", "common", "Elite Dangerous", ProductsSubPath), GameStore.Steam);
        }
    }

    static IEnumerable<GameInstall> FromEpic()
    {
        var manifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(manifests)) {
            yield break;
        }

        foreach (var item in Directory.EnumerateFiles(manifests, "*.item")) {
            string? location = null;
            try {
                using var doc = JsonDocument.Parse(File.ReadAllText(item));
                var root = doc.RootElement;
                var name = root.TryGetProperty("DisplayName", out var n) ? n.GetString() : null;
                if (name != null && name.Contains("Elite Dangerous", StringComparison.OrdinalIgnoreCase)) {
                    location = root.TryGetProperty("InstallLocation", out var l) ? l.GetString() : null;
                }
            }
            catch (Exception) {
                // A broken manifest of another game must not stop the search.
            }

            if (location != null) {
                yield return new GameInstall(Path.Combine(location, ProductsSubPath), GameStore.Epic);
            }
        }
    }

    static IEnumerable<GameInstall> FromFrontier()
    {
        foreach (var programs in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles }) {
            yield return new GameInstall(Path.Combine(Environment.GetFolderPath(programs), "Frontier", ProductsSubPath), GameStore.Frontier);
        }
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")]
    private static partial Regex VdfPath();
}
