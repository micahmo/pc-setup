using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCSetup.Services;

namespace PCSetup.ViewModels;

public sealed partial class RunPageViewModel : PageViewModel
{
    private CancellationTokenSource? _cts;

    public RunPageViewModel(MainViewModel main) : base(main, "Run", "")
    {
        Log.LineWritten += line => Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            LogLines.Add(line);
            if (LogLines.Count > 5000) LogLines.RemoveAt(0);
        });
    }

    public ObservableCollection<ItemViewModel> RunItems { get; } = [];
    public ObservableCollection<string> LogLines { get; } = [];
    public string LogFile => Log.FilePath;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsIdle), nameof(CanStart))] private bool _isRunning;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsIdle))] private bool _hasRun;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanStart))] private int _pendingCount;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _needsReboot;
    [ObservableProperty] private bool _needsSignOut;

    public bool IsIdle => !IsRunning && !HasRun;

    /// <summary>Run setup is only offered when something is actually queued.</summary>
    public bool CanStart => !IsRunning && PendingCount > 0;

    partial void OnIsRunningChanged(bool value) => Main.OnRunStateChanged();

    /// <summary>Shows what's queued before you start. After a run, the results stay until there's something new to run.</summary>
    public override void OnShown()
    {
        if (IsRunning) return;
        var todo = Queue();
        if (HasRun && todo.Count == 0) return;

        HasRun = false;
        Progress = 0;
        Status = todo.Count switch
        {
            0 => "Nothing to run yet. Select items on the other pages first.",
            1 => "1 item is ready to run.",
            _ => $"{todo.Count} items are ready to run.",
        };
    }

    private List<ItemViewModel> Queue()
    {
        RunItems.Clear();
        foreach (var item in ItemEngine.Sort(Main.ItemsToRun(), i => i.Item))
        {
            item.Run = RunState.Queued;
            item.RunMessage = "Queued";
            RunItems.Add(item);
        }
        PendingCount = RunItems.Count;
        return RunItems.ToList();
    }

    [RelayCommand]
    public async Task StartAsync()
    {
        if (IsRunning) return;

        if (Queue().Count == 0)
        {
            Status = "Nothing to run. Everything you selected is already done.";
            return;
        }

        IsRunning = true;
        NeedsReboot = NeedsSignOut = false;
        _cts = new CancellationTokenSource();
        var restartExplorer = false;

        try
        {
            if (Main.Review.CreateRestorePoint)
            {
                Status = "Creating a restore point…";
                await CreateRestorePointAsync(_cts.Token);
            }

            for (var n = 0; n < RunItems.Count; n++)
            {
                var item = RunItems[n];
                Status = $"{item.Name} ({n + 1} of {RunItems.Count})";
                Progress = 100.0 * n / RunItems.Count;

                if (await RunOneAsync(item, _cts.Token) && item.Run == RunState.Done)
                {
                    restartExplorer |= item.Item.Restart == "explorer";
                    NeedsReboot |= item.Item.Restart == "reboot";
                    NeedsSignOut |= item.Item.Restart == "signout";
                }
            }

            Progress = 100;
            if (restartExplorer && !NeedsReboot && !NeedsSignOut) await RestartExplorerAsync();
            Status = Summarize();
        }
        catch (OperationCanceledException)
        {
            Log.Write("Run canceled.");
            Status = "Canceled. Anything that finished stays done; run setup again to pick up the rest.";
            foreach (var item in RunItems.Where(i => i.Run is RunState.Queued or RunState.Running))
            {
                item.Run = RunState.Skipped;
                item.RunMessage = "Canceled";
            }
        }
        finally
        {
            PendingCount = 0;
            IsRunning = false;
            HasRun = true;
            _cts.Dispose();
            _cts = null;
        }
    }

    /// <summary>Returns true when the item's outcome is final for this run.</summary>
    private async Task<bool> RunOneAsync(ItemViewModel item, CancellationToken ct)
    {
        var blocker = item.Item.DependsOn
            .Select(Main.FindItem)
            .FirstOrDefault(d => d is not null && !d.IsDone && !(RunItems.Contains(d) && d.Run == RunState.Done));
        if (blocker is not null)
        {
            item.Run = RunState.Skipped;
            item.RunMessage = $"Waiting on {blocker.Name}";
            Log.Write($"[{item.Item.Id}] skipped: needs {blocker.Item.Id}");
            return false;
        }

        item.Run = RunState.Running;
        item.RunMessage = "Running…";
        Log.Write($"[{item.Item.Id}] {item.Name}");

        var outcome = await ItemEngine.ApplyAsync(item.Item, line => Log.Write($"  {line}"), ct);
        (item.Run, item.RunMessage) = outcome switch
        {
            ApplyOutcome.Done => (RunState.Done, "Done"),
            ApplyOutcome.Manual => (RunState.Manual, "Do this yourself"),
            ApplyOutcome.NotReady => (RunState.NotReady, item.Item.Requires?.Hint ?? "Not ready yet"),
            _ => (RunState.Failed, "Failed. See the log."),
        };
        if (outcome == ApplyOutcome.Done)
        {
            item.Scan = ScanState.Done;
            item.RememberDone();
        }
        Log.Write($"[{item.Item.Id}] {outcome}");
        return true;
    }

    [RelayCommand]
    private async Task RetryAsync(ItemViewModel item)
    {
        if (IsRunning) return;
        IsRunning = true;
        _cts = new CancellationTokenSource();
        try
        {
            await RunOneAsync(item, _cts.Token);
            if (item.Run == RunState.Done && item.Item.Restart == "explorer") await RestartExplorerAsync();
            NeedsReboot |= item.Run == RunState.Done && item.Item.Restart == "reboot";
            NeedsSignOut |= item.Run == RunState.Done && item.Item.Restart == "signout";
            Status = Summarize();
        }
        catch (OperationCanceledException) { }
        finally
        {
            IsRunning = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void Reboot() => Process.Start("shutdown.exe", "/r /t 5");

    [RelayCommand]
    private void SignOut() => Process.Start("shutdown.exe", "/l");

    private string Summarize()
    {
        int Count(RunState s) => RunItems.Count(i => i.Run == s);
        var parts = new List<string> { $"{Count(RunState.Done)} done" };
        if (Count(RunState.Failed) > 0) parts.Add($"{Count(RunState.Failed)} failed");
        var waiting = Count(RunState.NotReady) + Count(RunState.Skipped);
        if (waiting > 0) parts.Add($"{waiting} waiting");
        if (Count(RunState.Manual) > 0) parts.Add($"{Count(RunState.Manual)} for you to do");
        var summary = string.Join(", ", parts) + ".";
        Log.Write($"Finished: {summary}");
        return summary;
    }

    private static async Task CreateRestorePointAsync(CancellationToken ct)
    {
        Log.Write("Creating a restore point");
        // Windows allows one restore point per 24 hours unless the frequency limit is lifted.
        var result = await ProcessRunner.PowerShellAsync("""
            Enable-ComputerRestore -Drive "$env:SystemDrive\"
            Set-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore' -Name SystemRestorePointCreationFrequency -Value 0 -Type DWord
            Checkpoint-Computer -Description 'PC Setup' -RestorePointType MODIFY_SETTINGS
            """, line => Log.Write($"  {line}"), ct);
        if (result.ExitCode != 0) Log.Write("  Restore point failed; continuing without one.");
    }

    private static async Task RestartExplorerAsync()
    {
        Log.Write("Restarting Explorer");
        foreach (var p in Process.GetProcessesByName("explorer"))
        {
            try { p.Kill(); } catch { }
        }
        // Windows restarts the shell on its own, as you rather than elevated. Only start it if that didn't happen.
        await Task.Delay(TimeSpan.FromSeconds(4));
        if (Process.GetProcessesByName("explorer").Length == 0)
            ProcessRunner.Open("explorer.exe");
    }
}
