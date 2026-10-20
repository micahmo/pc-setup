using PCSetup.Models;

namespace PCSetup.Services;

public enum ApplyOutcome { Done, NotReady, Failed, Manual }

/// <summary>Checks and applies catalog items. Every apply is followed by a fresh check, so "done" always means verified.</summary>
public static class ItemEngine
{
    public static async Task<bool> IsDoneAsync(CatalogItem item, HashSet<string>? wingetInstalled, CancellationToken ct = default)
    {
        // An explicit detect script always wins, e.g. for apps that can also arrive outside winget.
        if (item.Detect is not null) return await ProcessRunner.DetectAsync(item.Detect, ct);

        return item.Kind switch
        {
            ItemKind.Winget when wingetInstalled is not null && wingetInstalled.Contains(item.Winget!) => true,
            ItemKind.Winget => await WingetService.IsInstalledAsync(item, ct),
            ItemKind.Registry => RegistryService.IsApplied(item.Registry!),
            _ => false,
        };
    }

    public static async Task<bool> IsReadyAsync(CatalogItem item, CancellationToken ct = default) =>
        item.Requires is null || await ProcessRunner.DetectAsync(item.Requires.Test, ct);

    public static async Task<ApplyOutcome> ApplyAsync(CatalogItem item, Action<string> log, CancellationToken ct)
    {
        if (item.Kind == ItemKind.Manual) return ApplyOutcome.Manual;

        if (!await IsReadyAsync(item, ct))
        {
            log($"Not ready: {item.Requires!.Hint ?? item.Requires.Test}");
            return ApplyOutcome.NotReady;
        }

        var shortcutsBefore = DesktopShortcuts();
        try
        {
            switch (item.Kind)
            {
                case ItemKind.Winget:
                    if (!await WingetService.InstallAsync(item, log, ct)) return ApplyOutcome.Failed;
                    break;
                case ItemKind.Registry:
                    foreach (var value in item.Registry!) RegistryService.Apply(value, log);
                    break;
                case ItemKind.Script:
                    var result = await ProcessRunner.PowerShellAsync(item.Apply!, log, ct);
                    if (result.ExitCode != 0)
                    {
                        log($"Script exited with {result.ExitCode}");
                        return ApplyOutcome.Failed;
                    }
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log($"Error: {ex.Message}");
            return ApplyOutcome.Failed;
        }
        finally
        {
            RemoveNewShortcuts(shortcutsBefore, log);
        }

        // Items without a check can only trust the exit code.
        if (!item.CanDetect) return ApplyOutcome.Done;
        if (await IsDoneAsync(item, null, ct)) return ApplyOutcome.Done;

        log("Applied, but the check still says it isn't done.");
        return ApplyOutcome.Failed;
    }

    private static readonly string[] Desktops =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
    ];

    private static HashSet<string> DesktopShortcuts() =>
        Desktops.Where(System.IO.Directory.Exists)
            .SelectMany(d => System.IO.Directory.EnumerateFiles(d, "*.lnk").Concat(System.IO.Directory.EnumerateFiles(d, "*.url")))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Installers love adding desktop shortcuts even when silent. Remove any this install created; leave existing ones alone.</summary>
    private static void RemoveNewShortcuts(HashSet<string> before, Action<string> log)
    {
        foreach (var file in DesktopShortcuts().Where(f => !before.Contains(f)))
        {
            try
            {
                System.IO.File.Delete(file);
                log($"Removed desktop shortcut {System.IO.Path.GetFileName(file)}");
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
            {
                log($"Couldn't remove desktop shortcut {file}: {ex.Message}");
            }
        }
    }

    /// <summary>Orders items by Order, then catalog position, with dependencies moved ahead of the items that need them.</summary>
    public static List<T> Sort<T>(IEnumerable<T> items, Func<T, CatalogItem> get)
    {
        var pending = items.OrderBy(i => get(i).Order).ThenBy(i => get(i).Index).ToList();
        var ids = pending.Select(i => get(i).Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sorted = new List<T>();

        while (pending.Count > 0)
        {
            var next = pending.FirstOrDefault(i => get(i).DependsOn.All(d => !ids.Contains(d) || placed.Contains(d)))
                ?? pending[0]; // a cycle: just take the next one
            pending.Remove(next);
            placed.Add(get(next).Id);
            sorted.Add(next);
        }
        return sorted;
    }
}
