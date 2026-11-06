using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace GenFrame.Setup.Services;

// One value of Elite's graphics files. Tag: an element of Custom.4.4.fxcfg. OverridePath: elements below the root
// of GraphicsConfigurationOverride.xml. Choices go from low to high.
public sealed record GraphicsOption(int Tier, string Label, string Hint, (string Value, string Text)[] Choices,
    string? Tag = null, string[]? OverridePath = null)
{
    public string Key => Tag ?? string.Join('/', OverridePath!);
}

// Four levels for the graphics page. Performance and Standard take Frontier's High and Ultra (OptionDefaults\*.fxcfg).
// Anti-aliasing, supersampling, upscaling, FOV and blur are left alone: EDAA depends on the first three.
public sealed record GraphicsPreset(string Name, string Hint, Dictionary<string, string> Values);

// Quality values beyond Elite's graphics menu.
public sealed class GraphicsOptions(string graphicsFolder)
{
    public static readonly GraphicsOption[] All = [
        new(1, "Terrain", "UltraPlus zeigt mehr Details auf Planeten. Mittlere Kosten.", [("2", "High"), ("3", "Ultra"), ("4", "UltraPlus")], Tag: "TerrainQuality"),
        new(1, "Triebwerkskegel: Ultra", "Feinere Triebwerke. Vermutlich geringe Kosten.", [("2", "High"), ("3", "Ultra")], Tag: "JetConeQuality"),
        new(1, "Volumetrie: Ultra", "Nebel und Ringe in voller Auflösung. Vermutlich am teuersten, bei Rucklern zuerst abschalten.", [("2", "High"), ("3", "Ultra")], Tag: "VolumetricsQuality"),
        new(1, "Reflexionen (Envmap)", "Schärfere Reflexionen im Lack. 2048 ist noch nicht getestet.",
            [("512", "512"), ("1024", "1024"), ("2048", "2048 (Stufe 2)")], OverridePath: ["Envmap", "High", "TextureSize"]),
        new(2, "Sichtweite", "Bei 1,5 ploppen Details später auf. Mehr Last für CPU und Grafikkarte.", [("0.900000", "0,9"), ("1.000000", "1,0"), ("1.500000", "1,5")], Tag: "LODDistanceScale"),
        new(2, "Planetentexturen: 8192", "Schärfere Planeten. Kann bei 12 GB Grafikspeicher knapp werden, dann ruckelt es.",
            [("4096", "4096"), ("8192", "8192")], OverridePath: ["Planets", "Ultra", "TextureSize"]),
    ];

    const string Terrain = "TerrainQuality", Jet = "JetConeQuality", Volumetrics = "VolumetricsQuality", Distance = "LODDistanceScale";
    const string Envmap = "Envmap/High/TextureSize", Planets = "Planets/Ultra/TextureSize";

    static Dictionary<string, string> Level(int shadows, int detail, int effects, int galaxy, int dof, int blend, int sampler, int filter,
        int terrain, string distance, int envmap) => new() {
        ["DirectionalShadowQuality"] = $"{shadows}", ["SpotShadowQuality"] = $"{shadows}",
        ["AOQuality"] = $"{effects}", ["BloomQuality"] = $"{effects}", ["EnvironmentQuality"] = $"{effects}",
        ["FXQuality"] = $"{effects}", ["ParticleEffectsQuality"] = $"{effects}", ["SurfaceMaterialQuality"] = $"{effects}",
        ["MaterialQuality"] = $"{detail}", ["GalaxyMapQuality"] = $"{galaxy}", ["DOFEnabled"] = $"{dof}",
        ["TerrainLodBlendingQuality"] = $"{blend}", ["SurfaceSamplerQuality"] = $"{sampler}", ["TextureFilterQuality"] = $"{filter}",
        [Terrain] = $"{terrain}", [Jet] = terrain == 2 ? "2" : "3", [Volumetrics] = terrain == 2 ? "2" : "3",
        [Distance] = distance, [Envmap] = $"{envmap}", [Planets] = "4096",
    };

    public static readonly GraphicsPreset[] Presets = [
        new("Performance", "Frontiers „High“. Mehr Reserve, sichtbar weniger Details.", Level(3, 1, 2, 1, 1, 1, 2, 3, 2, "0.900000", 512)),
        new("Standard", "Frontiers „Ultra“, das Maximum des Spielmenüs.", Level(4, 3, 3, 2, 2, 2, 3, 4, 3, "1.000000", 512)),
        new("Quality", "Standard plus Terrain UltraPlus und Reflexionen 1024. Gemessen auf einer RTX 4070 Ti: Station etwa 71 %, Ring bis 94 % Auslastung.", Level(4, 3, 3, 2, 2, 2, 3, 4, 4, "1.000000", 1024)),
        new("Quality+", "Quality plus Sichtweite 1,5. Noch nicht gemessen, im Ring wird es vermutlich eng.", Level(4, 3, 3, 2, 2, 2, 3, 4, 4, "1.500000", 1024)),
    ];

    static GraphicsOption OptionFor(string key) =>
        All.FirstOrDefault(o => o.Key == key) ?? new GraphicsOption(0, key, "", [], Tag: key);

    public static IEnumerable<(GraphicsOption Option, string Value)> Values(GraphicsPreset preset) =>
        preset.Values.Select(v => (OptionFor(v.Key), v.Value));

    // The preset whose values are all in the files; null when something was changed by hand.
    public GraphicsPreset? CurrentPreset => Presets.FirstOrDefault(p => p.Values.All(v => Same(Read(OptionFor(v.Key)), v.Value)));

    static bool Same(string? a, string b) =>
        double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
        && double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out var y) && x == y;

    string CustomXml => Path.Combine(graphicsFolder, "Custom.4.4.fxcfg");
    string OverrideXml => Path.Combine(graphicsFolder, "GraphicsConfigurationOverride.xml");
    string SettingsXml => Path.Combine(graphicsFolder, "Settings.xml");

    // Custom.4.4.fxcfg only counts while Elite's preset is "Custom" (assumption from the file's name and PresetName).
    public string? ActivePreset => EliteCheck.XmlValue(SettingsXml, "PresetName");

    public bool Available => File.Exists(CustomXml);

    public string? Read(GraphicsOption option)
    {
        if (option.Tag != null) {
            return EliteCheck.XmlValue(CustomXml, option.Tag);
        }
        if (!File.Exists(OverrideXml)) {
            return null;
        }
        XElement? element = XDocument.Load(OverrideXml).Root;
        foreach (var name in option.OverridePath!) {
            element = element?.Element(name);
        }
        return element?.Value.Trim();
    }

    // The highest choice the value reaches, 0 when it is unset or lower.
    public static int ChoiceIndex(GraphicsOption option, string? value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) {
            return 0;
        }
        var index = 0;
        for (var i = 0; i < option.Choices.Length; i++) {
            if (number >= double.Parse(option.Choices[i].Value, CultureInfo.InvariantCulture)) {
                index = i;
            }
        }
        return index;
    }

    public void Write(IEnumerable<(GraphicsOption Option, string Value)> values)
    {
        var list = values.ToList();
        foreach (var (option, value) in list.Where(v => v.Option.Tag != null)) {
            EliteCheck.SetXml(CustomXml, option.Tag!, value);
        }

        var overrides = list.Where(v => v.Option.OverridePath != null).ToList();
        if (overrides.Count == 0) {
            return;
        }
        var document = File.Exists(OverrideXml)
            ? XDocument.Load(OverrideXml, LoadOptions.PreserveWhitespace)
            : new XDocument(new XElement("GraphicsConfig"));
        foreach (var (option, value) in overrides) {
            var element = document.Root!;
            foreach (var name in option.OverridePath!) {
                var child = element.Element(name);
                if (child == null) {
                    child = new XElement(name);
                    element.Add(child);
                }
                element = child;
            }
            element.Value = value;
        }
        // Frontier's own header and line ends stay; XDocument would rewrite the declaration.
        File.WriteAllText(OverrideXml, "<?xml version=\"1.0\" encoding=\"UTF-8\" ?>\n" + document.Root!.ToString(SaveOptions.DisableFormatting) + "\n",
            new UTF8Encoding(false));
    }
}
