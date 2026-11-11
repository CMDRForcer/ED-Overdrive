using System.Diagnostics;
using System.IO;

namespace GenFrame.Setup.Services;

// The tool only changes files while Elite is closed.
public static class GameState
{
    public static bool IsRunning()
    {
        var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(GameLocator.ExeName));
        foreach (var p in processes) {
            p.Dispose();
        }
        return processes.Length > 0;
    }

    public static string JournalFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Saved Games", "Frontier Developments", "Elite Dangerous");

    // Last session, from the newest journal: start time, end time and whether it ended with "Shutdown".
    public static (DateTime Start, DateTime End, bool CleanShutdown)? LastSession()
    {
        if (!Directory.Exists(JournalFolder)) {
            return null;
        }

        var journal = new DirectoryInfo(JournalFolder).GetFiles("Journal.*.log")
            .OrderBy(f => f.LastWriteTime).LastOrDefault();
        if (journal == null) {
            return null;
        }

        string last;
        using (var stream = new FileStream(journal.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(stream)) {
            last = reader.ReadToEnd().TrimEnd().Split('\n').LastOrDefault() ?? "";
        }

        return (journal.CreationTime, journal.LastWriteTime, last.Contains("\"event\":\"Shutdown\""));
    }
}
