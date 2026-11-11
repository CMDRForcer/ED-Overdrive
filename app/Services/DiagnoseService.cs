using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace GenFrame.Setup.Services;

// Collects what is needed to look at a problem into one zip. Journals stay out (commander name, position).
// Text files get the user folder replaced, dumps cannot be cleaned and are only added on request.
public sealed class DiagnoseService(GameInstall game, string graphicsFolder, Action<string> log)
{
    static string DumpFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps");

    public static FileInfo? NewestDump()
    {
        return Directory.Exists(DumpFolder)
            ? new DirectoryInfo(DumpFolder).GetFiles("EliteDangerous64*.dmp").OrderBy(f => f.LastWriteTime).LastOrDefault()
            : null;
    }

    public List<string> TextFiles()
    {
        var files = new List<string>();
        void Add(string folder, params string[] names) =>
            files.AddRange(names.Select(n => Path.Combine(folder, n)).Where(File.Exists));

        Add(game.Folder, "ReShade.log", "sl.log", "ReShade.ini", "ReShade2.ini", "EliteDangerous64.exe.watchdog.log");
        files.AddRange(Directory.EnumerateFiles(game.Folder, "GenFrame_*.ini"));
        Add(graphicsFolder, "Settings.xml", "DisplaySettings.xml", "GraphicsConfigurationOverride.xml", "Custom.4.4.fxcfg");
        return files;
    }

    public void Create(string zipPath, bool includeDump)
    {
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        WriteEntry(zip, "Bericht.txt", Clean(Report()));
        log("Bericht geschrieben.");

        foreach (var file in TextFiles()) {
            var folder = file.StartsWith(graphicsFolder, StringComparison.OrdinalIgnoreCase) ? "Grafik" : "Spielordner";
            WriteEntry(zip, $"{folder}/{Path.GetFileName(file)}", Clean(ReadShared(file)));
            log($"{Path.GetFileName(file)} hinzugefügt.");
        }

        if (includeDump && NewestDump() is { } dump) {
            log($"Dump {dump.Name} wird gepackt ({dump.Length / 1048576} MB) …");
            zip.CreateEntryFromFile(dump.FullName, $"Dump/{dump.Name}", CompressionLevel.Fastest);
        }
    }

    string Report()
    {
        var text = new StringBuilder();
        text.AppendLine($"ED-Overdrive, Diagnose vom {DateTime.Now:dd.MM.yyyy HH:mm}");
        text.AppendLine($"Windows {Environment.OSVersion.Version}");
        text.AppendLine($"Spielordner: {game.Folder} ({game.Store})");
        text.AppendLine($"Windows-HDR: {DisplayInfo.HdrEnabled()?.ToString() ?? "unbekannt"}, Hauptmonitor: {DisplayInfo.RefreshRate()?.ToString() ?? "?"} Hz");
        text.AppendLine();

        text.AppendLine("Komponenten:");
        foreach (var c in InstallInfo.Scan(game.Folder)) {
            text.AppendLine($"  {c.Name}: {c.State}, {c.Detail}");
        }
        if (InstallInfo.LoadedVersion(game.Folder) is { } loaded) {
            text.AppendLine($"  Zuletzt geladen: {loaded}");
        }
        text.AppendLine();

        text.AppendLine("Letzte Sitzung:");
        var session = GameState.LastSession();
        text.AppendLine(session is { } s ? $"  {s.Start:dd.MM. HH:mm}–{s.End:HH:mm}, sauber beendet: {s.CleanShutdown}" : "  unbekannt");
        text.AppendLine();

        text.AppendLine("Elite-Check:");
        foreach (var result in new EliteCheck(game, graphicsFolder).Run()) {
            text.AppendLine($"  [{result.Status}] {result.Group} / {result.Title}: {result.Detail}");
        }
        text.AppendLine();

        text.AppendLine("Treiberprofil von Elite:");
        try {
            var state = NvDrs.Read();
            if (!state.ProfileFound) {
                text.AppendLine("  kein Profil");
            }
            foreach (var entry in state.Entries) {
                text.AppendLine($"  0x{entry.Id:X8}: eigener Wert {(entry.Own is { } own ? $"0x{own:X}" : "keiner")}, wirksam 0x{entry.Effective:X}");
            }
        }
        catch (Exception e) {
            // A report on a system without NVIDIA driver is still a report.
            text.AppendLine("  nicht lesbar: " + e.Message);
        }
        return text.ToString();
    }

    static void WriteEntry(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    // The game may be writing its logs right now.
    static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    static string Clean(string text)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var form in new[] { profile, profile.Replace('\\', '/') }) {
            text = Regex.Replace(text, Regex.Escape(form), "%USERPROFILE%", RegexOptions.IgnoreCase);
        }
        return text;
    }
}
