using System.IO;
using PCSetup.Models;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PCSetup.Services;

public sealed class CatalogException(string message) : Exception(message);

public static class CatalogLoader
{
    // Unknown keys are errors, so a typo in a catalog file is reported instead of silently ignored.
    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    private static readonly ISerializer Writer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitEmptyCollections | DefaultValuesHandling.OmitNull)
        .Build();

    public static string AppDir => AppContext.BaseDirectory;

    /// <summary>Per-PC settings. Outside the app folder, so updating the app keeps them.</summary>
    public static string LocalSettingsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCSetup", "settings.yaml");

    /// <summary>pcsetup.yaml from the app folder, plus preset folders remembered on this PC.</summary>
    public static AppSettings LoadSettings()
    {
        var path = Path.Combine(AppDir, "pcsetup.yaml");
        var settings = File.Exists(path) ? Parse<AppSettings>(path) : new AppSettings();
        settings.PresetFolders.AddRange(LoadLocalSettings().PresetFolders);
        return settings;
    }

    /// <summary>Remembers a folder a preset was loaded from, so it's searched automatically next time.</summary>
    public static void RememberPresetFolder(string folder)
    {
        var local = LoadLocalSettings();
        if (local.PresetFolders.Contains(folder, StringComparer.OrdinalIgnoreCase)) return;
        local.PresetFolders.Add(folder);
        SaveLocalSettings(local);
    }

    /// <summary>Records (or clears) "Mark done" for a manual step on this PC.</summary>
    public static void SetManualDone(string id, bool done)
    {
        var local = LoadLocalSettings();
        local.ManualDone.RemoveAll(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
        if (done) local.ManualDone.Add(id);
        SaveLocalSettings(local);
    }

    public static LocalSettings LoadLocalSettings()
    {
        try
        {
            return File.Exists(LocalSettingsPath) ? Parse<LocalSettings>(LocalSettingsPath) : new LocalSettings();
        }
        catch (CatalogException)
        {
            return new LocalSettings();
        }
    }

    public static void SaveLocalSettings(LocalSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LocalSettingsPath)!);
        File.WriteAllText(LocalSettingsPath, Writer.Serialize(settings));
    }

    public static List<CatalogItem> LoadCatalog()
    {
        var dir = Path.Combine(AppDir, "catalog");
        if (!Directory.Exists(dir))
            throw new CatalogException($"Catalog folder not found: {dir}");

        var items = new List<CatalogItem>();
        foreach (var file in Directory.GetFiles(dir, "*.yaml", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
        {
            var catalog = Parse<CatalogFile>(file);
            foreach (var item in catalog.Items)
            {
                item.Section ??= catalog.Section ?? "Other";
                item.Category ??= catalog.Category ?? "General";
                item.SourceFile = file;
                items.Add(item);
            }
        }

        Validate(items);
        return items;
    }

    public static Preset LoadPreset(string file)
    {
        var preset = Parse<Preset>(file);
        preset.FilePath = file;
        if (string.IsNullOrWhiteSpace(preset.Name))
            preset.Name = Path.GetFileNameWithoutExtension(file);

        foreach (var item in preset.Items)
        {
            item.Section ??= preset.Section ?? preset.Name;
            item.Category ??= "General";
            item.SourceFile = file;
            item.PresetName = preset.Name;
        }

        Validate(preset.Items);
        return preset;
    }

    /// <summary>Finds preset files in the app's presets folder and any configured folders.</summary>
    public static List<string> FindPresetFiles(AppSettings settings)
    {
        var folders = new List<string> { Path.Combine(AppDir, "presets") };
        folders.AddRange(settings.PresetFolders.Select(Environment.ExpandEnvironmentVariables));

        return folders
            .Where(Directory.Exists)
            .SelectMany(f => Directory.GetFiles(f, "*.yaml"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static void SavePreset(Preset preset, string file) => File.WriteAllText(file, Writer.Serialize(preset));

    private static T Parse<T>(string file)
    {
        try
        {
            return Yaml.Deserialize<T>(File.ReadAllText(file)) ?? Activator.CreateInstance<T>();
        }
        catch (YamlException ex)
        {
            var reason = ex.InnerException?.Message ?? ex.Message;
            throw new CatalogException($"{Path.GetFileName(file)}, line {ex.Start.Line}: {reason}");
        }
    }

    private static void Validate(List<CatalogItem> items)
    {
        foreach (var item in items)
        {
            var where = $"{Path.GetFileName(item.SourceFile)} ({(item.Id.Length > 0 ? item.Id : item.Name)})";
            if (string.IsNullOrWhiteSpace(item.Id)) throw new CatalogException($"{where}: item is missing an id.");
            if (string.IsNullOrWhiteSpace(item.Name)) throw new CatalogException($"{where}: item is missing a name.");
            if (item.Restart is not ("none" or "explorer" or "signout" or "reboot"))
                throw new CatalogException($"{where}: restart must be none, explorer, signout, or reboot.");
        }

        var dupe = items.GroupBy(i => i.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        if (dupe is not null)
            throw new CatalogException($"Duplicate item id '{dupe.Key}' in {string.Join(", ", dupe.Select(i => Path.GetFileName(i.SourceFile)).Distinct())}.");
    }
}
