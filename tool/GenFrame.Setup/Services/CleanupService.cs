using System.IO;
using System.IO.Enumeration;

namespace GenFrame.Setup.Services;

// "Clean reinstall": moves everything ReShade, EDAA, Streamline and their leftovers put into
// the game folder into a folder of its own. Nothing is deleted; EDHM, RenoDX and game files stay.
public static class CleanupService
{
    static readonly string[] FilePatterns = [
        "dxgi.dll", "ReShade*.ini", "ReShade*.ini*", "*Preset*.ini", "*Preset*.ini_*", "GenFrame_*.ini", "ReShade*.log*",
        "EDAA.addon64*", "GenFrame.addon64*", "sl.*.dll", "sl.log*", "nvngx_dlss*.dll*", "libxess.dll",
        "renodx-dlss5*", "dlss5-*", "DLSS5*", "RenoDX-DLSS5*", "gitc-uplift*",
    ];
    // GITC-Uplift: DLSS 5 comes back with one click on its page, the DLL is kept in the app's folder.
    static readonly string[] FolderNames = ["reshade-shaders", "GITC-Uplift", InstallService.LicenseFolder];

    // Never touched, even if a pattern above would match.
    static readonly string[] Keep = ["renodx-elitedangerous.addon64", "d3d11.dll", "d3dx.ini", "d3dcompiler_47.dll"];

    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GenFrame", "Entfernt");

    public static List<string> FindOldFiles(GameInstall game)
    {
        var files = Directory.EnumerateFiles(game.Folder)
            .Where(f => {
                var name = Path.GetFileName(f);
                return !Keep.Contains(name, StringComparer.OrdinalIgnoreCase)
                    && FilePatterns.Any(p => FileSystemName.MatchesSimpleExpression(p, name, ignoreCase: true));
            });
        var folders = FolderNames.Select(n => Path.Combine(game.Folder, n)).Where(Directory.Exists);
        return files.Concat(folders).ToList();
    }

    public static string MoveOldFiles(GameInstall game, Action<string> log)
    {
        var target = Path.Combine(Root, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(target);

        var items = FindOldFiles(game);
        foreach (var item in items) {
            var destination = Path.Combine(target, Path.GetFileName(item));
            if (Directory.Exists(item)) {
                CopyFolder(item, destination);
                Directory.Delete(item, recursive: true);
            }
            else {
                // Copy and compare before removing: the game folder can be on another drive.
                File.Copy(item, destination);
                if (new FileInfo(destination).Length != new FileInfo(item).Length) {
                    throw new IOException($"Kopie von {Path.GetFileName(item)} ist unvollständig – Abbruch.");
                }
                File.Delete(item);
            }
        }
        log($"{items.Count} alte Dateien/Ordner verschoben nach {target}.");
        return target;
    }

    static void CopyFolder(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source)) {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
        foreach (var folder in Directory.EnumerateDirectories(source)) {
            CopyFolder(folder, Path.Combine(destination, Path.GetFileName(folder)));
        }
    }
}
