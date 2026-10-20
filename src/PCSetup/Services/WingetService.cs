using System.IO;
using System.Text.Json;
using PCSetup.Models;

namespace PCSetup.Services;

public static class WingetService
{
    private const string Common = "--accept-source-agreements --disable-interactivity";

    // winget exit codes that still mean "the package is there".
    private static readonly HashSet<int> AlreadyInstalledCodes =
    [
        unchecked((int)0x8A15002B), // no applicable update
        unchecked((int)0x8A150061), // package already installed
    ];

    public static async Task<bool> IsAvailableAsync()
    {
        var result = await ProcessRunner.RunAsync("winget", "--version", null);
        return result.ExitCode == 0;
    }

    /// <summary>One `winget export` call is much faster than a `winget list` per app.</summary>
    public static async Task<HashSet<string>> GetInstalledIdsAsync(CancellationToken ct = default)
    {
        var file = Path.Combine(Path.GetTempPath(), "PCSetup", $"winget-export-{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await ProcessRunner.RunAsync("winget", $"export -o \"{file}\" {Common}", null, ct);
            if (!File.Exists(file)) return ids;

            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(file, ct));
            foreach (var source in doc.RootElement.GetProperty("Sources").EnumerateArray())
            foreach (var package in source.GetProperty("Packages").EnumerateArray())
                ids.Add(package.GetProperty("PackageIdentifier").GetString() ?? "");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or IOException)
        {
            // Fall back to per-item checks.
        }
        finally
        {
            try { File.Delete(file); } catch { }
        }
        return ids;
    }

    public static async Task<bool> IsInstalledAsync(CatalogItem item, CancellationToken ct = default)
    {
        var result = await ProcessRunner.RunAsync("winget", $"list --id {item.Winget} -e {SourceArg(item)} {Common}", null, ct);
        return result.ExitCode == 0;
    }

    public static async Task<bool> InstallAsync(CatalogItem item, Action<string> log, CancellationToken ct)
    {
        var args = $"install --id {item.Winget} -e {SourceArg(item)} --silent --accept-package-agreements {Common}";
        if (item.Scope is not null) args += $" --scope {item.Scope}";
        if (item.WingetArgs is not null) args += $" {item.WingetArgs}";

        log($"winget {args}");
        var result = await ProcessRunner.RunAsync("winget", args, line =>
        {
            // Skip winget's spinner and progress-bar lines.
            var trimmed = line.Trim();
            if (trimmed.Length > 2 && !trimmed.All(c => c is '-' or '\\' or '|' or '/' or '█' or '▒' or ' ') && !trimmed.Contains('█'))
                log(trimmed);
        }, ct);

        if (result.ExitCode == 0 || AlreadyInstalledCodes.Contains(result.ExitCode)) return true;
        log($"winget exited with 0x{result.ExitCode:X8}");
        return false;
    }

    private static string SourceArg(CatalogItem item) => item.Source is null ? "" : $"--source {item.Source}";
}
