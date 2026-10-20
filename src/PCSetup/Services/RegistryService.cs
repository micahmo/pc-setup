using System.Globalization;
using Microsoft.Win32;
using PCSetup.Models;

namespace PCSetup.Services;

public static class RegistryService
{
    public static bool IsApplied(IEnumerable<RegistryValue> values) => values.All(IsApplied);

    public static bool IsApplied(RegistryValue v)
    {
        using var key = OpenKey(v.Path, writable: false);
        var current = key?.GetValue(v.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (v.Delete) return current is null;
        if (current is null) return false;

        return ParseKind(v.Type) switch
        {
            RegistryValueKind.DWord => current is int i && i == unchecked((int)ParseUInt(v.Value)),
            RegistryValueKind.QWord => current is long l && l == unchecked((long)ParseULong(v.Value)),
            RegistryValueKind.Binary => current is byte[] b && b.SequenceEqual(ParseBytes(v.Value)),
            _ => string.Equals(current.ToString(), v.Value ?? "", StringComparison.OrdinalIgnoreCase),
        };
    }

    public static void Apply(RegistryValue v, Action<string> log)
    {
        if (v.Delete)
        {
            using var existing = OpenKey(v.Path, writable: true);
            existing?.DeleteValue(v.Name, throwOnMissingValue: false);
            log($"Deleted {v.Path}\\{v.Name}");
            return;
        }

        var (hive, subKey) = Split(v.Path);
        using var key = hive.CreateSubKey(subKey, writable: true)
            ?? throw new InvalidOperationException($"Couldn't open {v.Path}");

        var kind = ParseKind(v.Type);
        object data = kind switch
        {
            RegistryValueKind.DWord => unchecked((int)ParseUInt(v.Value)),
            RegistryValueKind.QWord => unchecked((long)ParseULong(v.Value)),
            RegistryValueKind.Binary => ParseBytes(v.Value),
            _ => v.Value ?? "",
        };
        key.SetValue(v.Name, data, kind);
        log($"Set {v.Path}\\{v.Name} = {v.Value}");
    }

    private static RegistryKey? OpenKey(string path, bool writable)
    {
        var (hive, subKey) = Split(path);
        return hive.OpenSubKey(subKey, writable);
    }

    private static (RegistryKey Hive, string SubKey) Split(string path)
    {
        var normalized = path.Replace(":\\", "\\").TrimEnd('\\');
        var slash = normalized.IndexOf('\\');
        var hiveName = slash < 0 ? normalized : normalized[..slash];
        var subKey = slash < 0 ? "" : normalized[(slash + 1)..];

        var hive = hiveName.ToUpperInvariant() switch
        {
            "HKCU" or "HKEY_CURRENT_USER" => RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64),
            "HKLM" or "HKEY_LOCAL_MACHINE" => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64),
            "HKCR" or "HKEY_CLASSES_ROOT" => RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64),
            "HKU" or "HKEY_USERS" => RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64),
            _ => throw new CatalogException($"Unknown registry hive in '{path}'."),
        };
        return (hive, subKey);
    }

    private static RegistryValueKind ParseKind(string type) => type.ToLowerInvariant() switch
    {
        "dword" => RegistryValueKind.DWord,
        "qword" => RegistryValueKind.QWord,
        "string" => RegistryValueKind.String,
        "expandstring" => RegistryValueKind.ExpandString,
        "binary" => RegistryValueKind.Binary,
        _ => throw new CatalogException($"Unknown registry type '{type}'."),
    };

    private static uint ParseUInt(string? s) => (uint)ParseULong(s);

    private static ulong ParseULong(string? s)
    {
        s = (s ?? "0").Trim();
        return s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? ulong.Parse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : ulong.Parse(s, CultureInfo.InvariantCulture);
    }

    private static byte[] ParseBytes(string? s) =>
        (s ?? "").Split([' ', ',', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Select(h => byte.Parse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture))
            .ToArray();
}
