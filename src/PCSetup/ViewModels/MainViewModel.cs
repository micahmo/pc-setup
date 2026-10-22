using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PCSetup.Models;
using PCSetup.Services;

namespace PCSetup.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private AppSettings _settings = new();
    private List<CatalogItem> _catalog = [];
    private readonly Dictionary<string, ScanState> _scanCache = new(StringComparer.OrdinalIgnoreCase);
    private bool _suppressPresetEvents;

    public MainViewModel()
    {
        Start = new StartPageViewModel(this);
        Review = new ReviewPageViewModel(this);
        RunPage = new RunPageViewModel(this);
        Pages.Add(Start);
        Pages.Add(Review);
        Pages.Add(RunPage);
        _currentPage = Start;
    }

    public StartPageViewModel Start { get; }
    public ReviewPageViewModel Review { get; }
    public RunPageViewModel RunPage { get; }

    public ObservableCollection<PageViewModel> Pages { get; } = [];
    public ObservableCollection<PresetOption> Presets { get; } = [];
    public List<ItemViewModel> Items { get; private set; } = [];

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanGoBack), nameof(NextLabel), nameof(ShowNext))]
    private PageViewModel _currentPage;

    /// <summary>While scanning, moving on would show half-checked items, so Next and the step list wait.</summary>
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(BackCommand)), NotifyPropertyChangedFor(nameof(NextLabel), nameof(CanNavigate))]
    private bool _isScanning;

    /// <summary>The step list and Back are locked while scanning or running; Cancel is the way out of a run.</summary>
    public bool CanNavigate => !IsScanning && !RunPage.IsRunning;

    public void OnRunStateChanged()
    {
        OnPropertyChanged(nameof(CanNavigate));
        BackCommand.NotifyCanExecuteChanged();
    }
    [ObservableProperty] private string _scanSummary = "Checking this PC…";
    [ObservableProperty] private string? _error;
    [ObservableProperty] private bool _wingetMissing;
    [ObservableProperty] private bool _isDarkMode = CatalogLoader.LoadLocalSettings().Theme != "light";

    partial void OnIsDarkModeChanged(bool value)
    {
        App.ApplyTheme(value);
        try
        {
            var local = CatalogLoader.LoadLocalSettings();
            local.Theme = value ? "dark" : "light";
            CatalogLoader.SaveLocalSettings(local);
        }
        catch (IOException ex)
        {
            Log.Write($"Couldn't save the theme: {ex.Message}");
        }
    }

    public string Version => typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "";
    public bool CanGoBack => Pages.IndexOf(CurrentPage) > 0;
    public bool ShowNext => CurrentPage != RunPage;
    public string NextLabel => IsScanning ? "Checking…" : CurrentPage == Review ? "Run setup" : "Next";

    partial void OnCurrentPageChanged(PageViewModel value)
    {
        value.OnShown();
        NextCommand.NotifyCanExecuteChanged();
    }

    public async Task InitializeAsync()
    {
        Log.Write($"PC Setup {Version} on {Environment.OSVersion.VersionString} ({Environment.MachineName}, {Environment.UserDomainName}\\{Environment.UserName})");
        Log.Write($"App folder: {CatalogLoader.AppDir}");
        try
        {
            _settings = CatalogLoader.LoadSettings();
            _catalog = CatalogLoader.LoadCatalog();
            Log.Write($"Loaded {_catalog.Count} catalog items");
        }
        catch (CatalogException ex)
        {
            ShowError(ex.Message);
            return;
        }

        LoadPresetFiles(CatalogLoader.FindPresetFiles(_settings));
        Rebuild();

        WingetMissing = !await WingetService.IsAvailableAsync();
        if (WingetMissing) Log.Write("winget isn't available");
        await ScanAsync();
    }

    public void ShowError(string message)
    {
        Error = message;
        Log.Write($"Error: {message}");
    }

    private void LoadPresetFiles(IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            if (Presets.Any(p => string.Equals(p.FilePath, file, StringComparison.OrdinalIgnoreCase))) continue;
            try
            {
                Presets.Add(new PresetOption(CatalogLoader.LoadPreset(file), this));
                Log.Write($"Found preset: {file}");
            }
            catch (CatalogException ex)
            {
                ShowError($"Preset skipped. {ex.Message}");
            }
        }
    }

    public void OnPresetsChanged()
    {
        if (_suppressPresetEvents) return;
        Log.Write($"Active presets: {string.Join(", ", Presets.Where(p => p.IsActive).Select(p => p.Name).DefaultIfEmpty("none"))}");
        Rebuild();
        _ = ScanAsync();
    }

    /// <summary>Merges the catalog with active presets, applies selections, and rebuilds the wizard steps.</summary>
    private void Rebuild()
    {
        // Keep what the last scan or run learned, except for items a preset may have replaced.
        foreach (var old in Items.Where(i => i.Scan != ScanState.Checking)) _scanCache[old.Item.Id] = old.Scan;
        foreach (var presetItem in Presets.SelectMany(p => p.Preset.Items)) _scanCache.Remove(presetItem.Id);

        var active = Presets.Where(p => p.IsActive).Select(p => p.Preset).ToList();
        var merged = _catalog.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);
        var order = _catalog.Select(i => i.Id).ToList();
        foreach (var item in active.SelectMany(p => p.Items))
        {
            if (!merged.ContainsKey(item.Id)) order.Add(item.Id);
            merged[item.Id] = item;
        }

        Items = order.Select((id, n) => { var item = merged[id]; item.Index = n; return new ItemViewModel(item); }).ToList();
        foreach (var vm in Items)
        {
            // Nothing starts selected unless an item says default: true or a preset selects it.
            vm.IsSelected = vm.Item.Default;
            if (_scanCache.TryGetValue(vm.Item.Id, out var cached)) vm.Scan = cached;
        }
        foreach (var preset in active)
        {
            var all = preset.Select.Contains("*");
            foreach (var vm in Items.Where(i => all || preset.Select.Contains(i.Item.Id, StringComparer.OrdinalIgnoreCase)))
                vm.IsSelected = true;
            foreach (var vm in Items.Where(i => preset.Deselect.Contains(i.Item.Id, StringComparer.OrdinalIgnoreCase)))
                vm.IsSelected = false;
        }

        // Manual steps stay on the to-do list until they're done or marked done.
        var marked = MarkedDone();
        foreach (var vm in Items.Where(i => i.IsManual)) vm.IsSelected = true;
        foreach (var vm in Items.Where(i => i.IsRemembered)) vm.IsMarkedDone = vm.IsDone && marked.Contains(vm.Item.Id);

        var sections = Items.Select(i => i.Section).Distinct().ToList();
        sections = _settings.SectionOrder.Where(sections.Contains)
            .Concat(sections.Where(s => !_settings.SectionOrder.Contains(s)))
            .ToList();

        var keep = CurrentPage;
        while (Pages.Count > 3) Pages.RemoveAt(1);
        for (var n = 0; n < sections.Count; n++)
            Pages.Insert(1 + n, new SectionPageViewModel(this, sections[n], Items.Where(i => i.Section == sections[n]).ToList()));
        CurrentPage = keep is SectionPageViewModel ? Start : keep;
    }

    private bool _rescanQueued;

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning)
        {
            _rescanQueued = true;
            return;
        }
        IsScanning = true;
        ScanSummary = "Checking this PC…";
        try
        {
            do
            {
                _rescanQueued = false;
                await ScanPendingAsync();
            } while (_rescanQueued);
        }
        finally
        {
            IsScanning = false;
        }

        var done = Items.Count(i => i.IsDone);
        ScanSummary = $"{done} of {Items.Count} items are already done on this PC.";
        foreach (var page in Pages.OfType<SectionPageViewModel>()) page.OnShown();
    }

    private async Task ScanPendingAsync()
    {
        var todo = Items.Where(i => !_scanCache.ContainsKey(i.Item.Id)).ToList();
        foreach (var vm in todo) vm.Scan = ScanState.Checking;

        HashSet<string>? installed = null;
        if (!WingetMissing && todo.Any(i => i.Item.Kind == ItemKind.Winget && i.Item.Detect is null))
            installed = await WingetService.GetInstalledIdsAsync();

        var marked = MarkedDone();
        using var throttle = new SemaphoreSlim(6);
        await Task.WhenAll(todo.Select(async vm =>
        {
            await throttle.WaitAsync();
            try
            {
                var state = await CheckAsync(vm.Item, installed);
                vm.IsMarkedDone = vm.IsRemembered && state != ScanState.Done && marked.Contains(vm.Item.Id);
                vm.Scan = vm.IsMarkedDone ? ScanState.Done : state;
            }
            finally { throttle.Release(); }
            _scanCache[vm.Item.Id] = vm.Scan;
            Log.Write($"Scan {vm.Item.Id}: {vm.Scan}");
        }));
    }

    private static HashSet<string> MarkedDone() =>
        CatalogLoader.LoadLocalSettings().ManualDone.ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static async Task<ScanState> CheckAsync(CatalogItem item, HashSet<string>? installed)
    {
        try
        {
            if (item.CanDetect && await ItemEngine.IsDoneAsync(item, installed)) return ScanState.Done;
            if (!await ItemEngine.IsReadyAsync(item)) return ScanState.NotReady;
            return item.Kind == ItemKind.Manual ? ScanState.Manual : ScanState.NotDone;
        }
        catch (Exception ex)
        {
            Log.Write($"Scan {item.Id} failed: {ex.Message}");
            return item.Kind == ItemKind.Manual ? ScanState.Manual : ScanState.NotDone;
        }
    }

    [RelayCommand]
    private async Task RescanAsync()
    {
        _scanCache.Clear();
        _suppressPresetEvents = true;
        try { LoadPresetFiles(CatalogLoader.FindPresetFiles(_settings)); }
        finally { _suppressPresetEvents = false; }
        await ScanAsync();
    }

    [RelayCommand]
    private void LoadPreset()
    {
        var dialog = new OpenFileDialog { Title = "Load a preset", Filter = "Preset (*.yaml)|*.yaml" };
        if (dialog.ShowDialog() != true) return;

        LoadPresetFiles([dialog.FileName]);
        var option = Presets.FirstOrDefault(p => string.Equals(p.FilePath, dialog.FileName, StringComparison.OrdinalIgnoreCase));
        if (option is null) return;
        option.IsActive = true;

        try
        {
            var folder = Path.GetDirectoryName(dialog.FileName)!;
            CatalogLoader.RememberPresetFolder(folder);
            Log.Write($"Remembered preset folder {folder}");
        }
        catch (IOException ex)
        {
            Log.Write($"Couldn't remember the preset folder: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        Directory.CreateDirectory(Log.Folder);
        ProcessRunner.Open(Log.Folder);
    }

    [RelayCommand]
    private void DismissError() => Error = null;

    [RelayCommand]
    private void Navigate(PageViewModel page) => CurrentPage = page;

    [RelayCommand(CanExecute = nameof(CanNavigate))]
    private void Back()
    {
        var n = Pages.IndexOf(CurrentPage);
        if (n > 0) CurrentPage = Pages[n - 1];
    }

    // On Review, Next is "Run setup", which only makes sense when something is queued.
    private bool CanGoNext() => !IsScanning && !(CurrentPage == Review && Review.IsEmpty);

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextAsync()
    {
        if (CurrentPage == Review)
        {
            CurrentPage = RunPage;
            await RunPage.StartAsync();
            return;
        }
        var n = Pages.IndexOf(CurrentPage);
        if (n < Pages.Count - 1) CurrentPage = Pages[n + 1];
    }

    /// <summary>Keeps the search the same on every step; setting a page's text to what it already has does nothing.</summary>
    public void ShareSearch(string text)
    {
        foreach (var page in Pages.OfType<SectionPageViewModel>()) page.SearchText = text;
    }

    public ItemViewModel? FindItem(string id) =>
        Items.FirstOrDefault(i => string.Equals(i.Item.Id, id, StringComparison.OrdinalIgnoreCase));

    // Manual steps are done from their own page (Open, Mark done), so a run only holds what the app does itself.
    public List<ItemViewModel> ItemsToRun() =>
        Items.Where(i => i.IsSelected && !i.IsDone && !i.IsManual && i.Scan != ScanState.Checking).ToList();

    public List<string> DependencyWarnings(List<ItemViewModel> todo) =>
        todo.SelectMany(i => i.Item.DependsOn
                .Select(FindItem)
                .Where(d => d is not null && !d.IsDone && !todo.Contains(d))
                .Select(d => $"{i.Name} needs {d!.Name}, which isn't selected."))
            .Concat(todo.Where(i => i.Scan == ScanState.NotReady)
                .Select(i => $"{i.Name}: {i.Item.Requires?.Hint ?? "not ready yet"}"))
            .Concat(FollowUpWarnings(Items, todo))
            .ToList();

    /// <summary>
    /// Apps being installed without the settings that go with them (items that depend on the app), which are easy
    /// to forget since they're on a later page. Other installs that depend on an app, like add-ons, don't count.
    /// </summary>
    public static IEnumerable<string> FollowUpWarnings(IEnumerable<ItemViewModel> items, List<ItemViewModel> todo)
    {
        var installs = todo.Where(i => i.Item.Kind == ItemKind.Winget).ToList();
        foreach (var followUp in items.Where(f => f.Item.Kind != ItemKind.Winget && !f.IsDone && !todo.Contains(f)))
        {
            var apps = installs.Where(a => followUp.Item.DependsOn.Contains(a.Item.Id, StringComparer.OrdinalIgnoreCase)).ToList();
            if (apps.Count == 0) continue;
            var names = string.Join(" and ", apps.Select(a => a.Name));
            yield return followUp.IsManual
                ? $"{names} will be installed. Afterward, remember “{followUp.Name}” on the {followUp.Section} page."
                : $"{names} will be installed, but “{followUp.Name}” isn't selected.";
        }
    }
}
