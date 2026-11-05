using System.IO;
using System.Text;

namespace GenFrame.Setup.Services;

// Minimal ini editor that keeps order, unknown lines and comments (ReShade rewrites the file itself,
// so it only has to survive our own edits).
public sealed class IniFile
{
    readonly List<string> lines = [];

    public static IniFile Load(string path)
    {
        var ini = new IniFile();
        ini.lines.AddRange(File.ReadAllLines(path));
        return ini;
    }

    public string? Get(string section, string key)
    {
        var index = FindKey(section, key);
        return index < 0 ? null : lines[index][(key.Length + 1)..];
    }

    public void Set(string section, string key, string value)
    {
        var index = FindKey(section, key);
        if (index >= 0) {
            lines[index] = $"{key}={value}";
            return;
        }

        var start = lines.FindIndex(l => l.Trim() == $"[{section}]");
        if (start < 0) {
            if (lines.Count > 0 && lines[^1].Length > 0) {
                lines.Add("");
            }
            lines.Add($"[{section}]");
            lines.Add($"{key}={value}");
            return;
        }

        // Insert after the last non-empty line of the section.
        var end = start + 1;
        while (end < lines.Count && !lines[end].StartsWith('[')) {
            end++;
        }
        while (end > start + 1 && lines[end - 1].Trim().Length == 0) {
            end--;
        }
        lines.Insert(end, $"{key}={value}");
    }

    public void Save(string path) =>
        File.WriteAllText(path, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(false));

    int FindKey(string section, string key)
    {
        bool inSection = false;
        for (int i = 0; i < lines.Count; i++) {
            var line = lines[i].Trim();
            if (line.StartsWith('[')) {
                inSection = line == $"[{section}]";
            }
            else if (inSection && line.StartsWith(key + "=")) {
                return i;
            }
        }
        return -1;
    }
}
