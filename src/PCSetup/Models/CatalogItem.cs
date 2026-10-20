using YamlDotNet.Serialization;

namespace PCSetup.Models;

public enum ItemKind
{
    Winget,
    Registry,
    Script,
    Manual,
}

/// <summary>One thing the app can check and (usually) apply. Defined in catalog/*.yaml or in a preset.</summary>
public sealed class CatalogItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>Wizard step this item appears on. Defaults to the file's section.</summary>
    public string? Section { get; set; }

    /// <summary>Group heading within the step. Defaults to the file's category.</summary>
    public string? Category { get; set; }

    /// <summary>Selected when no preset says otherwise.</summary>
    public bool Default { get; set; }

    // winget items
    public string? Winget { get; set; }
    public string? Source { get; set; }
    public string? Scope { get; set; }
    public string? WingetArgs { get; set; }

    // registry items
    public List<RegistryValue>? Registry { get; set; }

    // script items (PowerShell). Detect returns $true when the item is already done.
    public string? Detect { get; set; }
    public string? Apply { get; set; }

    // manual items
    public string? Instructions { get; set; }

    /// <summary>A URL, ms-settings: link, or program to open from the item's "Open" button.</summary>
    public string? Open { get; set; }

    /// <summary>A precondition that must pass before apply runs (e.g. a synced folder exists).</summary>
    public Requirement? Requires { get; set; }

    public List<string> DependsOn { get; set; } = [];

    /// <summary>none, explorer, signout, or reboot.</summary>
    public string Restart { get; set; } = "none";

    /// <summary>Lower runs first. Use a high number for cleanup that must follow installs.</summary>
    public int Order { get; set; } = 50;

    /// <summary>
    /// A fresh-PC step (like unpinning everything): once it has run, or you mark it done, it stays done on
    /// that PC even if its check would later fail.
    /// </summary>
    public bool Once { get; set; }

    [YamlIgnore] public string SourceFile { get; set; } = "";
    [YamlIgnore] public string? PresetName { get; set; }
    [YamlIgnore] public int Index { get; set; }

    [YamlIgnore]
    public ItemKind Kind =>
        Winget is not null ? ItemKind.Winget
        : Registry is { Count: > 0 } ? ItemKind.Registry
        : Apply is not null ? ItemKind.Script
        : ItemKind.Manual;

    [YamlIgnore]
    public bool CanDetect => Kind is ItemKind.Winget or ItemKind.Registry || Detect is not null;
}

public sealed class RegistryValue
{
    /// <summary>HKCU\..., HKLM\..., or the PowerShell form HKCU:\...</summary>
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>DWord, QWord, String, ExpandString, or Binary (hex bytes).</summary>
    public string Type { get; set; } = "DWord";
    public string? Value { get; set; }

    /// <summary>Remove the value instead of setting it.</summary>
    public bool Delete { get; set; }
}

public sealed class Requirement
{
    public string Test { get; set; } = "";
    public string? Hint { get; set; }
}

/// <summary>The shape of a catalog/*.yaml file.</summary>
public sealed class CatalogFile
{
    public string? Section { get; set; }
    public string? Category { get; set; }
    public List<CatalogItem> Items { get; set; } = [];
}
