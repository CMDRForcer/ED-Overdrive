using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace GenFrame.Setup.Services;

// DLSS 5 (NVIDIA's neural rendering) through GITC Uplift, a ReShade add-on (MIT). Uplift ships inside the exe;
// NVIDIA's nvngx_dlssnr.dll may not be passed on, so the user picks it once and the app keeps a copy.
// Tested 08.11.2026 on an RTX 4070 Ti: Uplift hooks EDAA's own NGX D3D11 DLSS call and uses its motion vectors.
public static class UpliftService
{
    public const string Version = "1.8.0";
    const string ZipSha256 = "ec72c7f844f831dfaf647c051983a4ee1452582d1deeef0bf6ed6e89a19170d8";
    const string AddonSha256 = "3c939d99e22d1039bfc82222fd8dbc432db493d13ccec34efa26ef8cc39bf773";
    const string Resource = "GITC-Uplift.zip";

    const string AddonName = "gitc-uplift.addon64";
    const string HelperName = "gitc-uplift-helper64.exe";
    const string FolderName = "GITC-Uplift";
    public const string RuntimeName = "nvngx_dlssnr.dll";
    static readonly string[] Shaders = ["Uplift.fx", "UpliftMask.fx"];

    // Render scale with DLSS 5: Elite's step closest to DLSS Quality's optimum (0.67). DLSS 5 runs at that size
    // before EDAA's DLSS upscales. 0.65 chosen on 08.11.2026 over 0.75: no visible loss, fewer pixels for all three.
    public const double RenderScale = 0.65;

    public sealed record Runtime(string Label, bool ForRtx40);

    // nvngx_dlssnr.dll builds seen so far, by SHA-256. Same version number, different files.
    // 310.8 only has the Legacy network; 310.9.16 adds Small FP8 (Uplift's network preset B).
    static readonly Dictionary<string, Runtime> KnownRuntimes = new(StringComparer.OrdinalIgnoreCase) {
        ["cdfa6c1ec784867182164c7836f324f087b5c9f374d0c9fe46f54999feabf3ed"] =
            new("310.9.16 compat-fp16 (Community, unsigniert), Small FP8 auf RTX 4070 Ti getestet", true),
        ["e67dee209320cdafe0e93e45675d7aa34323a53acc57a72b2e40a181581c989a"] =
            new("310.8 angepasst für RTX 20/30/40 (RenoDX-Discord), nur Legacy, auf RTX 4070 Ti getestet", true),
        ["8270b350cd82de5ce89806872cdd6b6a9249b80836b91bbeb3573470744cc206"] =
            new("angepasster Build für RTX 40 (DLSS5-Feeder Issue #131), nicht selbst getestet", true),
        ["e16bcf15e16e13f527491cdf7845b2fe6521a738d8f7c9c721866a8496e1fc8e"] =
            new("NVIDIA-Original, läuft nur auf RTX 50", false),
    };

    public static string StoreFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GenFrame", "DLSS5");
    public static string StoredRuntime => Path.Combine(StoreFolder, RuntimeName);

    static string ReShadeIni(GameInstall game) => Path.Combine(game.Folder, "ReShade.ini");
    static string CustomXml => Path.Combine(EliteCheck.DefaultGraphicsFolder, "Custom.4.4.fxcfg");

    public sealed record State(bool Installed, bool CurrentVersion, string? RuntimeHash, Runtime? Runtime, bool Enabled,
        bool BeforeUpscaling, bool KeepFaces, double? Scale, bool EdaaDlss, bool FilterActive);

    public static State Read(GameInstall game)
    {
        var addon = Path.Combine(game.Folder, AddonName);
        var runtime = Path.Combine(game.Folder, FolderName, RuntimeName);
        var hash = File.Exists(runtime) ? Sha256(runtime) : null;
        var ini = File.Exists(ReShadeIni(game)) ? IniFile.Load(ReShadeIni(game)) : null;
        return new State(
            File.Exists(addon),
            File.Exists(addon) && Sha256(addon) == AddonSha256,
            hash,
            hash != null ? Identify(hash) : null,
            ini?.Get("Uplift", "Enabled") == "1",
            ini?.Get("Uplift", "PreUpscale") == "1",
            ini?.Get("Uplift", "KeepFaces") != "0",
            double.TryParse(EliteCheck.XmlValue(CustomXml, "SSAAMultiplier"), NumberStyles.Float, CultureInfo.InvariantCulture, out var s) ? s : null,
            ini?.Get("EDAA", "aa_method") == "1",
            FilterService.Active(game) == FilterVariant.Dlss5);
    }

    public static Runtime? Identify(string hash) => KnownRuntimes.GetValueOrDefault(hash);

    // Keeps a copy of the user's DLL, so installs and reinstalls need no second pick.
    public static Runtime? StoreRuntime(string path)
    {
        if (!Path.GetFileName(path).Equals(RuntimeName, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException($"Bitte die Datei {RuntimeName} wählen.");
        }
        Directory.CreateDirectory(StoreFolder);
        File.Copy(path, StoredRuntime, overwrite: true);
        return Identify(Sha256(StoredRuntime));
    }

    public static void Install(GameInstall game, Action<string> log)
    {
        EnsureClosed();
        if (!File.Exists(StoredRuntime)) {
            throw new InvalidOperationException($"Zuerst {RuntimeName} auswählen.");
        }

        using var zipStream = typeof(UpliftService).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidDataException("Uplift fehlt in dieser App-Version.");
        var zipBytes = new byte[zipStream.Length];
        zipStream.ReadExactly(zipBytes);
        if (Convert.ToHexStringLower(SHA256.HashData(zipBytes)) != ZipSha256) {
            throw new InvalidDataException("Das mitgelieferte Uplift-Paket ist beschädigt.");
        }

        var backup = BackupService.Create("Vor DLSS 5 (Uplift)", Pages.BackupsPage.FilesToSave(game).Concat(Files(game)));
        log($"Backup angelegt: {backup.Files.Count} Dateien.");

        var shaders = Path.Combine(game.Folder, "reshade-shaders", "Shaders");
        var folder = Path.Combine(game.Folder, FolderName);
        Directory.CreateDirectory(shaders);
        Directory.CreateDirectory(folder);
        using (var zip = new ZipArchive(new MemoryStream(zipBytes))) {
            Extract(zip, "Addons/" + AddonName, Path.Combine(game.Folder, AddonName));
            Extract(zip, "Addons/" + HelperName, Path.Combine(game.Folder, HelperName));
            foreach (var shader in Shaders) {
                Extract(zip, "Shaders/" + shader, Path.Combine(shaders, shader));
            }
            Extract(zip, "LICENSE", Path.Combine(folder, "LICENSE-GITC-Uplift.txt"));
        }
        if (Sha256(Path.Combine(game.Folder, AddonName)) != AddonSha256) {
            throw new InvalidDataException("gitc-uplift.addon64 stimmt nach dem Entpacken nicht.");
        }
        File.Copy(StoredRuntime, Path.Combine(folder, RuntimeName), overwrite: true);
        log($"GITC Uplift {Version} und {RuntimeName} installiert.");

        Optimize(game, log);
    }

    // What the test on 08.11.2026 showed: DLSS 5 needs EDAA's DLSS call, runs cheapest before upscaling,
    // and the "Lebendig" filters double what it already does. Elite has almost no faces, so the face mask goes.
    public static void Optimize(GameInstall game, Action<string> log)
    {
        EnsureClosed();
        var path = ReShadeIni(game);
        var ini = File.Exists(path) ? IniFile.Load(path) : new IniFile();
        ini.Set("EDAA", "aa_method", "1");
        ini.Set("EDAA", "dlss_preset", "11"); // K: costs no more than J here and flickers less
        ini.Set("Uplift", "Enabled", "1");
        // "Match game": DLSS 5 works at the game's render size (looked best in the test). Value read from
        // Uplift 1.8.0's ReShade.ini after picking it in the overlay.
        ini.Set("Uplift", "ResolutionMode", "4");
        ini.Set("Uplift", "KeepFaces", "0");
        ini.Set("Uplift", "PreUpscale", "1");
        // Small FP8 needs NR 310.9.16; measured 08.11.2026 on an RTX 4070 Ti: 67 instead of 56 real frames.
        var smallFp8 = RuntimeVersion(game) >= new Version(310, 9, 16);
        ini.Set("Uplift", "Preset", smallFp8 ? "2" : "1");
        ini.Save(path);
        log($"EDAA auf DLSS (Modell K), DLSS 5 an in Spielauflösung, vor dem Hochskalieren, Gesichtsmaske aus, Netz {(smallFp8 ? "Small FP8" : "Legacy (DLL 310.8)")}.");

        if (File.Exists(CustomXml)) {
            EliteCheck.SetXml(CustomXml, "SSAAMultiplier", RenderScale.ToString("0.000000", CultureInfo.InvariantCulture));
            log($"Elite rendert mit {RenderScale.ToString("0.00", CultureInfo.GetCultureInfo("de-DE"))}, EDAA skaliert mit DLSS hoch.");
        }

        new FilterService(game, log).SetActive(FilterVariant.Dlss5);
        log("Filter: „DLSS 5“ (Deband, leichte Schärfe).");
    }

    public static void SetEnabled(GameInstall game, bool on)
    {
        EnsureClosed();
        var ini = IniFile.Load(ReShadeIni(game));
        ini.Set("Uplift", "Enabled", on ? "1" : "0");
        ini.Save(ReShadeIni(game));
    }

    // Nothing is deleted: the files go to "Entfernt" like a clean reinstall. Render scale and filters go back to native.
    public static void Remove(GameInstall game, Action<string> log)
    {
        EnsureClosed();
        BackupService.Create("Vor Entfernen von DLSS 5", Pages.BackupsPage.FilesToSave(game));
        var target = Path.Combine(CleanupService.Root, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-DLSS5");
        Directory.CreateDirectory(target);
        foreach (var file in Files(game).Where(File.Exists)) {
            var relative = Path.GetRelativePath(game.Folder, file);
            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
            File.Delete(file);
        }
        var folder = Path.Combine(game.Folder, FolderName);
        if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) {
            Directory.Delete(folder);
        }
        log($"Uplift-Dateien verschoben nach {target}.");

        if (File.Exists(CustomXml)) {
            EliteCheck.SetXml(CustomXml, "SSAAMultiplier", "1.000000");
        }
        if (FilterService.Active(game) == FilterVariant.Dlss5) {
            new FilterService(game, log).SetActive(FilterVariant.Vivid);
        }
        log("Auflösung zurück auf 1,0, Filter zurück auf „Lebendig“.");
    }

    static IEnumerable<string> Files(GameInstall game)
    {
        yield return Path.Combine(game.Folder, AddonName);
        yield return Path.Combine(game.Folder, HelperName);
        yield return Path.Combine(game.Folder, FolderName, RuntimeName);
        yield return Path.Combine(game.Folder, FolderName, "LICENSE-GITC-Uplift.txt");
        foreach (var shader in Shaders) {
            yield return Path.Combine(game.Folder, "reshade-shaders", "Shaders", shader);
        }
    }

    static Version? RuntimeVersion(GameInstall game)
    {
        var path = Path.Combine(game.Folder, FolderName, RuntimeName);
        var info = File.Exists(path) ? System.Diagnostics.FileVersionInfo.GetVersionInfo(path) : null;
        return info == null ? null : new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart);
    }

    static void Extract(ZipArchive zip, string name, string target)
    {
        var entry = zip.GetEntry(name) ?? throw new InvalidDataException($"{name} fehlt im Uplift-Paket.");
        entry.ExtractToFile(target, overwrite: true);
    }

    static void EnsureClosed()
    {
        if (GameState.IsRunning()) {
            throw new InvalidOperationException("Elite läuft. Bitte das Spiel beenden und erneut versuchen.");
        }
    }

    static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
