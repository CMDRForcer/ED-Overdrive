using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GenFrame.Setup.Services;

public enum Channel { Stable, Development }

// Installs ReShade (add-on build), EDAA and Streamline into the game folder.
// Sources: see docs/tool-research.md.
public sealed class InstallService(GameInstall game, Action<string> log)
{
    // ReShade with full add-on support. The build is unsigned, so the download is pinned by hash.
    const string ReShadeVersion = "6.8.0";
    const string ReShadeUrl = "https://reshade.me/downloads/ReShade_Setup_6.8.0_Addon.exe";
    const string ReShadeSha256 = "afe4c8f13048306307983b8b3d41d5bf00a86820440b0e57dea10950e1176445";

    const string Repository = "CMDRForcer/EDAA";
    const string DevBranch = "builds/feature/fg-phase2";
    const string AddonName = "EDAA.addon64";

    static readonly string[] PackageFiles = [
        AddonName, "nvngx_dlss.dll", "nvngx_dlssg.dll", "libxess.dll",
        "sl.common.dll", "sl.dlss_g.dll", "sl.interposer.dll", "sl.pcl.dll", "sl.reflex.dll",
    ];
    static readonly string[] DevFiles = [
        AddonName, "streamline/nvngx_dlssg.dll", "streamline/sl.common.dll", "streamline/sl.dlss_g.dll",
        "streamline/sl.interposer.dll", "streamline/sl.pcl.dll", "streamline/sl.reflex.dll",
    ];

    static readonly HttpClient http = CreateClient();

    static string DownloadFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GenFrame", "Downloads");

    public async Task Install(Channel channel, bool clean)
    {
        EnsureGameClosed();
        Directory.CreateDirectory(DownloadFolder);

        // Download everything first: a broken connection must not leave a half-installed game.
        var reshade = await DownloadReShade();
        var package = await DownloadPackage();
        (string Folder, string Commit)? dev = channel == Channel.Development ? await DownloadDevBuild() : null;

        EnsureGameClosed();
        var disabledAddons = ReadDisabledAddons();

        var replaced = PackageFiles.Append("dxgi.dll").Select(f => Path.Combine(game.Folder, f));
        var backup = BackupService.Create(clean ? "Vor sauberer Neuinstallation" : "Vor Installation",
            Pages.BackupsPage.FilesToSave(game).Concat(replaced));
        log($"Backup angelegt: {backup.Files.Count} Dateien.");

        if (clean) {
            CleanupService.MoveOldFiles(game, log);
        }

        File.WriteAllBytes(Path.Combine(game.Folder, "dxgi.dll"), reshade);
        log($"ReShade {ReShadeVersion} (Add-on-Version) installiert.");

        using (var zip = ZipFile.OpenRead(package)) {
            foreach (var name in PackageFiles) {
                var entry = zip.GetEntry(name) ?? throw new InvalidDataException($"{name} fehlt im Paket.");
                entry.ExtractToFile(Path.Combine(game.Folder, name), overwrite: true);
            }
        }
        log("EDAA-Paket installiert (DLAA, XeSS, Streamline).");

        if (dev != null) {
            foreach (var file in Directory.EnumerateFiles(dev.Value.Folder)) {
                File.Copy(file, Path.Combine(game.Folder, Path.GetFileName(file)), overwrite: true);
            }
            log($"Entwicklungs-Build {dev.Value.Commit[..7]} darübergelegt.");
        }

        WriteReShadeIni(clean, disabledAddons);
        FilterService.WriteFgWindowConfig(game);
        log("ReShade2.ini für das FG-Fenster angelegt (ohne Effekte).");

        // A clean install ends ready to play: with the default filter variant.
        if (clean) {
            await new FilterService(game, log).Install(FilterVariant.Vivid);
        }
        log("Fertig. Elite kann gestartet werden.");
    }

    void EnsureGameClosed()
    {
        if (GameState.IsRunning()) {
            throw new InvalidOperationException("Elite läuft. Bitte das Spiel beenden und erneut versuchen.");
        }
    }

    async Task<byte[]> DownloadReShade()
    {
        var file = Path.Combine(DownloadFolder, Path.GetFileName(ReShadeUrl));
        if (!File.Exists(file) || Sha256(file) != ReShadeSha256) {
            log($"Lade ReShade {ReShadeVersion} …");
            await DownloadTo(ReShadeUrl, file);
        }
        if (Sha256(file) != ReShadeSha256) {
            File.Delete(file);
            throw new InvalidDataException("Der ReShade-Download hat eine unerwartete Prüfsumme und wurde verworfen.");
        }

        // Same as the ReShade installer: a zip is appended to the executable, starting at a 512-byte block.
        var data = File.ReadAllBytes(file);
        for (int offset = 0; offset + 30 <= data.Length; offset += 512) {
            if (data[offset] == 0x50 && data[offset + 1] == 0x4B && data[offset + 2] == 0x03 && data[offset + 3] == 0x04
                && data.AsSpan(offset + 4, 26).IndexOfAnyExcept((byte)0) >= 0) {
                using var zip = new ZipArchive(new MemoryStream(data, offset, data.Length - offset));
                var entry = zip.GetEntry("ReShade64.dll") ?? throw new InvalidDataException("ReShade64.dll fehlt im Installer.");
                using var stream = entry.Open();
                using var dll = new MemoryStream();
                stream.CopyTo(dll);
                return dll.ToArray();
            }
        }
        throw new InvalidDataException("Im ReShade-Installer wurde kein Archiv gefunden.");
    }

    // Newest published release (pre-releases included: the cmdr releases are pre-releases).
    async Task<string> DownloadPackage()
    {
        using var doc = JsonDocument.Parse(await http.GetStringAsync($"https://api.github.com/repos/{Repository}/releases"));
        foreach (var release in doc.RootElement.EnumerateArray()) {
            if (release.GetProperty("draft").GetBoolean()) {
                continue;
            }
            foreach (var asset in release.GetProperty("assets").EnumerateArray()) {
                var name = asset.GetProperty("name").GetString()!;
                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) {
                    var file = Path.Combine(DownloadFolder, name);
                    if (!File.Exists(file) || new FileInfo(file).Length != asset.GetProperty("size").GetInt64()) {
                        log($"Lade {name} …");
                        await DownloadTo(asset.GetProperty("browser_download_url").GetString()!, file);
                    }
                    log($"Paket: {release.GetProperty("tag_name").GetString()}");
                    return file;
                }
            }
        }
        throw new InvalidDataException("Kein Release-Paket gefunden.");
    }

    async Task<(string Folder, string Commit)> DownloadDevBuild()
    {
        string Raw(string path) => $"https://raw.githubusercontent.com/{Repository}/{DevBranch}/{path}";

        var commit = (await http.GetStringAsync(Raw("source_commit.txt"))).Trim();
        var folder = Path.Combine(DownloadFolder, "dev-" + commit[..7]);
        if (!Directory.Exists(folder)) {
            log($"Lade Entwicklungs-Build {commit[..7]} …");
            var temp = folder + ".part";
            Directory.CreateDirectory(temp);
            foreach (var path in DevFiles) {
                await DownloadTo(Raw(path), Path.Combine(temp, Path.GetFileName(path)));
            }
            // The DLSS DLL EDAA was built and tested with; older builds branches do not have it yet.
            try {
                await DownloadTo(Raw("nvngx_dlss.dll"), Path.Combine(temp, "nvngx_dlss.dll"));
            }
            catch (HttpRequestException) {
                log("nvngx_dlss.dll nicht im Entwicklungs-Build, die aus dem Paket bleibt.");
            }
            Directory.Move(temp, folder);
        }
        return (folder, commit);
    }

    List<string> ReadDisabledAddons()
    {
        var ini = Path.Combine(game.Folder, "ReShade.ini");
        if (!File.Exists(ini)) {
            return [];
        }
        var line = File.ReadLines(ini).FirstOrDefault(l => l.StartsWith("DisabledAddons="));
        return line == null ? [] : line["DisabledAddons=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    // Clean: a new ini with the defaults of docs/plan.md. Update: only add [EDAA] keys that are missing,
    // the user's own values stay.
    void WriteReShadeIni(bool clean, List<string> disabledAddons)
    {
        var path = Path.Combine(game.Folder, "ReShade.ini");
        var ini = clean || !File.Exists(path) ? new IniFile() : IniFile.Load(path);

        if (clean) {
            // Keep foreign add-ons (RenoDX) in the state the user chose.
            if (disabledAddons.Count > 0) {
                ini.Set("ADDON", "DisabledAddons", string.Join(",", disabledAddons));
            }
            ini.Set("GENERAL", "EffectSearchPaths", @".\reshade-shaders\Shaders\**");
            ini.Set("GENERAL", "TextureSearchPaths", @".\reshade-shaders\Textures\**");
            ini.Set("GENERAL", "SkipLoadingDisabledEffects", "0");
            ini.Set("GENERAL", "PreprocessorDefinitions", FilterService.DepthDefinitions);
            ini.Set("INPUT", "KeyOverlay", "36,0,0,0");
            ini.Set("OVERLAY", "TutorialProgress", "4");
        }

        foreach (var (key, value) in Defaults) {
            if (clean || ini.Get("EDAA", key) == null) {
                ini.Set("EDAA", key, value);
            }
        }
        ini.Save(path);
        log(clean ? "ReShade.ini mit Standardwerten neu angelegt." : "ReShade.ini ergänzt (deine Werte bleiben).");
    }

    // docs/plan.md: DLAA, model K, full FG HUD protection, frame generation on.
    static readonly (string Key, string Value)[] Defaults = [
        ("aa_method", "1"), ("dlss_preset", "11"), ("taa_before_hud", "1"), ("fg_hud_protect", "1"),
        ("fg_present_test", "1"), ("fg_factory", "1"), ("fg_generate", "1"), ("fg_fps_limit", "140"),
    ];

    async Task DownloadTo(string url, string file)
    {
        var temp = file + ".part";
        using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead)) {
            response.EnsureSuccessStatusCode();
            await using var output = File.Create(temp);
            await response.Content.CopyToAsync(output);
        }
        File.Move(temp, file, overwrite: true);
    }

    static string Sha256(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ED-Overdrive/0.1");
        return client;
    }
}
