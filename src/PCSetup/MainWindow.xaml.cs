using System.Windows;
using PCSetup.ViewModels;

namespace PCSetup;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        Loaded += async (_, _) => await _vm.InitializeAsync();
    }
}
