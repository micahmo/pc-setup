using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCSetup.Models;
using PCSetup.Services;

namespace PCSetup.ViewModels;

public enum ScanState { Checking, Done, NotDone, NotReady, Manual }

public enum RunState { Queued, Running, Done, Failed, NotReady, Skipped, Manual }

public sealed partial class ItemViewModel(CatalogItem item) : ObservableObject
{
    public CatalogItem Item { get; } = item;

    public string Name => Item.Name;
    public string Category => Item.Category ?? "";
    public string Section => Item.Section ?? "";
    public bool HasOpen => Item.Open is not null;
    public bool IsManual => Item.Kind == ItemKind.Manual;

    public string? Subtitle => Item.Description;

    /// <summary>Hover detail for the curious: which winget package an app comes from.</summary>
    public string? Details => Item.Kind == ItemKind.Winget ? $"winget: {Item.Winget}" : null;

    public string? Instructions => Item.Instructions?.Trim();

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsDone), nameof(CanToggle), nameof(ShowCheckbox), nameof(CanMarkDone),
        nameof(Badge), nameof(BadgeKind), nameof(BadgeTooltip))]
    private ScanState _scan = ScanState.Checking;

    [ObservableProperty] private bool _isSelected;

    /// <summary>A manual step you marked done yourself, rather than one the app detected.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Badge))]
    private bool _isMarkedDone;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(RunGlyph), nameof(RunKind), nameof(IsRunningNow))]
    private RunState _run = RunState.Queued;

    public bool IsRunningNow => Run == RunState.Running;

    [ObservableProperty] private string _runMessage = "Queued";

    public bool IsDone => Scan == ScanState.Done;

    /// <summary>Manual steps and one-time items can be marked done, and are remembered as done on this PC.</summary>
    public bool IsRemembered => IsManual || Item.Once;

    // Manual steps have no checkbox: they're always on your to-do list until done.
    public bool CanToggle => !IsManual && Scan != ScanState.Done && Scan != ScanState.Checking;
    public bool ShowCheckbox => !IsManual && !IsDone;
    public bool CanMarkDone => IsRemembered && !IsDone && Scan != ScanState.Checking;

    public string? Badge => Scan switch
    {
        ScanState.Done when IsMarkedDone => IsManual ? "Marked done" : "Done before",
        ScanState.Done => "Already done",
        ScanState.NotReady => "Not ready",
        ScanState.Manual => "Manual step",
        ScanState.Checking => "Checking…",
        _ => null,
    };

    public string BadgeKind => Scan switch
    {
        ScanState.Done => "success",
        ScanState.NotReady => "warning",
        _ => "neutral",
    };

    public string? BadgeTooltip => Scan == ScanState.NotReady ? Item.Requires?.Hint : Instructions;

    // Segoe Fluent Icons glyphs
    public string RunGlyph => Run switch
    {
        RunState.Running => "",
        RunState.Done => "",
        RunState.Failed => "",
        RunState.NotReady or RunState.Skipped => "",
        RunState.Manual => "",
        _ => "",
    };

    public string RunKind => Run switch
    {
        RunState.Done => "success",
        RunState.Failed => "danger",
        RunState.NotReady or RunState.Skipped or RunState.Manual => "warning",
        RunState.Running => "accent",
        _ => "neutral",
    };

    public bool CanRetry => !IsManual && Run is RunState.Failed or RunState.NotReady or RunState.Skipped;

    partial void OnRunChanged(RunState value) => OnPropertyChanged(nameof(CanRetry));

    /// <summary>Clicking anywhere on a row (not just the box) toggles it.</summary>
    [RelayCommand]
    private void Toggle()
    {
        if (CanToggle) IsSelected = !IsSelected;
    }

    [RelayCommand]
    private void MarkDone() => SetMarked(true);

    [RelayCommand]
    private void UndoMarkDone() => SetMarked(false);

    /// <summary>Called after a one-time item runs successfully, so later scans keep it done.</summary>
    public void RememberDone()
    {
        if (Item.Once) SetMarked(true);
    }

    private void SetMarked(bool done)
    {
        try
        {
            CatalogLoader.SetManualDone(Item.Id, done);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            Log.Write($"Couldn't save mark for {Item.Id}: {ex.Message}");
            return;
        }

        IsMarkedDone = done;
        Scan = done ? ScanState.Done : IsManual ? ScanState.Manual : ScanState.NotDone;
        if (done && Run is RunState.Manual or RunState.NotReady or RunState.Skipped)
        {
            Run = RunState.Done;
            RunMessage = "Marked done";
        }
        Log.Write($"[{Item.Id}] {(done ? "marked done" : "unmarked")}");
    }

    [RelayCommand]
    private void Open()
    {
        if (Item.Open is null) return;
        try { ProcessRunner.Open(Item.Open); }
        catch (Exception ex) { Log.Write($"Couldn't open {Item.Open}: {ex.Message}"); }
    }
}
