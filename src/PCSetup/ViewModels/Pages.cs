using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PCSetup.Models;

namespace PCSetup.ViewModels;

public abstract partial class PageViewModel(MainViewModel main, string title, string glyph) : ObservableObject
{
    public MainViewModel Main { get; } = main;
    public string Title { get; } = title;
    public string Glyph { get; } = glyph;

    public virtual void OnShown() { }
}

public sealed partial class PresetOption(Preset preset, MainViewModel main) : ObservableObject
{
    public Preset Preset { get; } = preset;
    public string Name => Preset.Name;
    public string? Description => Preset.Description;
    public string FilePath => Preset.FilePath;

    [ObservableProperty] private bool _isActive;

    partial void OnIsActiveChanged(bool value) => main.OnPresetsChanged();
}

public sealed class StartPageViewModel(MainViewModel main) : PageViewModel(main, "Start", "");

public sealed class CategoryGroup(string name, IEnumerable<ItemViewModel> items)
{
    public string Name { get; } = name;
    public List<ItemViewModel> Items { get; } = items.ToList();
}

public sealed partial class SectionPageViewModel : PageViewModel
{
    public SectionPageViewModel(MainViewModel main, string section, List<ItemViewModel> items)
        : base(main, section, GlyphFor(section))
    {
        Items = items;
        foreach (var item in items)
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(ItemViewModel.IsSelected) or nameof(ItemViewModel.Scan))
                    OnPropertyChanged(nameof(Summary));
            };
        Refresh();
    }

    public List<ItemViewModel> Items { get; }
    public ObservableCollection<CategoryGroup> Groups { get; } = [];
    public bool ShowSearch => Items.Count > 12;

    /// <summary>"Search apps", "Search Windows settings": sentence case, keeping proper nouns.</summary>
    public string SearchHint => Title.StartsWith("Windows", StringComparison.Ordinal)
        ? $"Search {Title}"
        : $"Search {char.ToLowerInvariant(Title[0])}{Title[1..]}";

    [ObservableProperty] private string _searchText = "";

    public string Summary
    {
        get
        {
            var done = Items.Count(i => i.IsDone);
            var selected = Items.Count(i => i.IsSelected && !i.IsDone);
            return $"{selected} selected · {done} already done · {Items.Count} total";
        }
    }

    partial void OnSearchTextChanged(string value) => Refresh();

    public override void OnShown() => OnPropertyChanged(nameof(Summary));

    public void Refresh()
    {
        Groups.Clear();
        var q = SearchText.Trim();
        var visible = Items.Where(i => q.Length == 0
            || i.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
            || (i.Subtitle?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
            || i.Category.Contains(q, StringComparison.OrdinalIgnoreCase));
        foreach (var g in visible.GroupBy(i => i.Category))
            Groups.Add(new CategoryGroup(g.Key, g));
        OnPropertyChanged(nameof(Summary));
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void SelectAll() => SetAll(true);

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void SelectNone() => SetAll(false);

    private void SetAll(bool value)
    {
        foreach (var g in Groups)
        foreach (var i in g.Items.Where(i => i.CanToggle))
            i.IsSelected = value;
        OnPropertyChanged(nameof(Summary));
    }

    private static string GlyphFor(string section) => section.ToLowerInvariant() switch
    {
        var s when s.Contains("app") => "",
        var s when s.Contains("setting") => "",
        var s when s.Contains("before") => "",
        var s when s.Contains("terminal") || s.Contains("shell") => "",
        var s when s.Contains("finish") || s.Contains("manual") => "",
        var s when s.Contains("work") => "",
        _ => "",
    };
}
