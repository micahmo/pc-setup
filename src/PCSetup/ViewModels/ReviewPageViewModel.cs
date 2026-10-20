using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PCSetup.Models;
using PCSetup.Services;

namespace PCSetup.ViewModels;

public sealed record ReviewGroup(string Section, List<ItemViewModel> Items);

public sealed partial class ReviewPageViewModel(MainViewModel main) : PageViewModel(main, "Review", "")
{
    [ObservableProperty] private List<ReviewGroup> _groups = [];
    [ObservableProperty] private List<string> _warnings = [];
    [ObservableProperty] private int _applyCount;
    [ObservableProperty] private string _restartText = "None";
    [ObservableProperty] private bool _createRestorePoint = true;

    public bool HasWarnings => Warnings.Count > 0;
    public bool IsEmpty => ApplyCount == 0;

    public override void OnShown()
    {
        var todo = Main.ItemsToRun();
        Groups = todo.GroupBy(i => i.Section).Select(g => new ReviewGroup(g.Key, g.ToList())).ToList();
        ApplyCount = todo.Count;
        Warnings = Main.DependencyWarnings(todo);

        var restarts = todo.Select(i => i.Item.Restart).ToHashSet();
        RestartText = restarts.Contains("reboot") ? "Reboot"
            : restarts.Contains("signout") ? "Sign out"
            : restarts.Contains("explorer") ? "Explorer only"
            : "None";

        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private void SavePreset()
    {
        // Start where presets were last loaded from on this PC.
        var remembered = CatalogLoader.LoadLocalSettings().PresetFolders.LastOrDefault(Directory.Exists);
        var dialog = new SaveFileDialog
        {
            Title = "Save selections as a preset",
            Filter = "Preset (*.yaml)|*.yaml",
            FileName = $"{Environment.MachineName.ToLowerInvariant()}.yaml",
            InitialDirectory = remembered ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog() != true) return;

        // Things already done on this PC count as wanted, so the preset recreates this machine.
        var all = Main.Items.Where(i => i.Item.PresetName is null && !i.IsManual).ToList();
        foreach (var done in all.Where(i => i.IsDone)) done.IsSelected = true;
        var preset = new Preset
        {
            Name = Path.GetFileNameWithoutExtension(dialog.FileName),
            Description = $"Saved from {Environment.MachineName} on {DateTime.Now:yyyy-MM-dd}",
            Select = all.Where(i => i.IsSelected && !i.Item.Default).Select(i => i.Item.Id).ToList(),
            Deselect = all.Where(i => !i.IsSelected && i.Item.Default && !i.IsDone).Select(i => i.Item.Id).ToList(),
        };
        try
        {
            CatalogLoader.SavePreset(preset, dialog.FileName);
            Log.Write($"Saved preset to {dialog.FileName}");
        }
        catch (Exception ex)
        {
            Main.ShowError($"Couldn't save the preset: {ex.Message}");
        }
    }
}
