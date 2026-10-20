using YamlDotNet.Serialization;

namespace PCSetup.Models;

/// <summary>
/// An optional layer over the catalog. It can pre-select or deselect catalog items and add
/// its own items. An added item with the same id as a catalog item replaces it.
/// </summary>
public sealed class Preset
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>Default step for this preset's own items.</summary>
    public string? Section { get; set; }

    /// <summary>Item ids to select. "*" selects everything.</summary>
    public List<string> Select { get; set; } = [];
    public List<string> Deselect { get; set; } = [];

    public List<CatalogItem> Items { get; set; } = [];

    [YamlIgnore] public string FilePath { get; set; } = "";
}

/// <summary>%LOCALAPPDATA%\PCSetup\settings.yaml: choices remembered on this PC, never in the repo.</summary>
public sealed class LocalSettings
{
    /// <summary>Folders presets were loaded from with "Load preset file…".</summary>
    public List<string> PresetFolders { get; set; } = [];

    /// <summary>dark or light.</summary>
    public string Theme { get; set; } = "dark";

    /// <summary>Ids of manual steps you've marked done on this PC.</summary>
    public List<string> ManualDone { get; set; } = [];
}

/// <summary>pcsetup.yaml next to the exe.</summary>
public sealed class AppSettings
{
    /// <summary>Order of the wizard steps. Sections not listed come after, in the order they're found.</summary>
    public List<string> SectionOrder { get; set; } = [];

    /// <summary>Folders searched for *.yaml presets. Environment variables are expanded.</summary>
    public List<string> PresetFolders { get; set; } = [];
}
