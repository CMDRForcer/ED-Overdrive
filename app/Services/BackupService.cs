using System.IO;
using System.Text.Json;

namespace GenFrame.Setup.Services;

public sealed record BackupFile(string Original, string Stored);

public sealed record Backup(string Folder, string Label, DateTime Created, List<BackupFile> Files);

// Every change the tool makes starts with a backup; nothing is ever deleted, only copied back.
public static class BackupService
{
    const string ManifestName = "backup.json";

    public static string Root => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GenFrame", "Backups");

    public static Backup Create(string label, IEnumerable<string> files)
    {
        var created = DateTime.Now;
        // Two backups in one second (the advisor's, then the game setup's) get "-2", "-3": each keeps its own files.
        var stamp = created.ToString("yyyyMMdd-HHmmss");
        var folder = Path.Combine(Root, stamp);
        for (var n = 2; Directory.Exists(folder); n++) {
            folder = Path.Combine(Root, $"{stamp}-{n}");
        }
        Directory.CreateDirectory(folder);

        var stored = new List<BackupFile>();
        int index = 0;
        foreach (var file in files.Where(File.Exists)) {
            // Prefix with an index: different folders can hold files with the same name.
            var name = $"{index++:D2}_{Path.GetFileName(file)}";
            File.Copy(file, Path.Combine(folder, name));
            stored.Add(new BackupFile(file, name));
        }

        var backup = new Backup(folder, label, created, stored);
        File.WriteAllText(Path.Combine(folder, ManifestName),
            JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true }));
        return backup;
    }

    public static List<Backup> List()
    {
        if (!Directory.Exists(Root)) {
            return [];
        }

        var backups = new List<Backup>();
        foreach (var folder in Directory.EnumerateDirectories(Root)) {
            var manifest = Path.Combine(folder, ManifestName);
            if (!File.Exists(manifest)) {
                continue;
            }
            try {
                var backup = JsonSerializer.Deserialize<Backup>(File.ReadAllText(manifest));
                if (backup != null) {
                    backups.Add(backup with { Folder = folder });
                }
            }
            catch (JsonException) {
                // Skip damaged manifests instead of failing the whole list.
            }
        }
        return backups.OrderByDescending(b => b.Created).ToList();
    }

    public static void Restore(Backup backup)
    {
        if (GameState.IsRunning()) {
            throw new InvalidOperationException("Elite läuft noch. Bitte das Spiel zuerst beenden.");
        }

        // The current state is saved first, so a restore can itself be undone.
        Create($"Vor Wiederherstellung: {backup.Label}", backup.Files.Select(f => f.Original));

        foreach (var file in backup.Files) {
            Directory.CreateDirectory(Path.GetDirectoryName(file.Original)!);
            File.Copy(Path.Combine(backup.Folder, file.Stored), file.Original, overwrite: true);
        }
    }
}
