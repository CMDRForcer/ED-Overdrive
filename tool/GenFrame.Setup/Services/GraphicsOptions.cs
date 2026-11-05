using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace GenFrame.Setup.Services;

// One value of Elite's graphics files. Tag: an element of Custom.4.4.fxcfg. OverridePath: elements below the root
// of GraphicsConfigurationOverride.xml. Choices[0] is the normal value, the later ones go higher.
public sealed record GraphicsOption(int Tier, string Label, string Hint, (string Value, string Text)[] Choices,
    string? Tag = null, string[]? OverridePath = null)
{
    public string Key => Tag ?? string.Join('/', OverridePath!);
}

// Quality values beyond Elite's graphics menu. Normal values: the state before the first test (03.11.2026).
public sealed class GraphicsOptions(string graphicsFolder)
{
    public static readonly GraphicsOption[] All = [
        new(1, "Terrain: UltraPlus", "Mehr Details auf Planeten. Mittlere Kosten.", [("3", "Ultra"), ("4", "UltraPlus")], Tag: "TerrainQuality"),
        new(1, "Triebwerkskegel: Ultra", "Feinere Triebwerke. Vermutlich geringe Kosten.", [("2", "High"), ("3", "Ultra")], Tag: "JetConeQuality"),
        new(1, "Volumetrie: Ultra", "Nebel und Ringe in voller Auflösung. Vermutlich am teuersten, bei Rucklern zuerst abschalten.", [("2", "High"), ("3", "Ultra")], Tag: "VolumetricsQuality"),
        new(1, "Reflexionen (Envmap)", "Schärfere Reflexionen im Lack. 2048 ist noch nicht getestet.",
            [("512", "512"), ("1024", "1024"), ("2048", "2048 (Stufe 2)")], OverridePath: ["Envmap", "High", "TextureSize"]),
        new(2, "Sichtweite: 1,5", "Details ploppen später auf. Mehr Last für CPU und Grafikkarte.", [("1.000000", "1,0"), ("1.500000", "1,5")], Tag: "LODDistanceScale"),
        new(2, "Planetentexturen: 8192", "Schärfere Planeten. Kann bei 12 GB Grafikspeicher knapp werden, dann ruckelt es.",
            [("4096", "4096"), ("8192", "8192")], OverridePath: ["Planets", "Ultra", "TextureSize"]),
    ];

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
