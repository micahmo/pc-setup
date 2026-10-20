using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using PCSetup.Services;

namespace PCSetup;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Write($"Crash: {args.Exception}");
            MessageBox.Show($"Something went wrong:\n\n{args.Exception.Message}\n\nDetails are in the log:\n{Log.FilePath}",
                "PC Setup", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Write($"Crash: {args.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Write($"Background error: {args.Exception}");
            args.SetObserved();
        };

        var elevated = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        Log.Write($"Elevated: {elevated}");

        ApplyTheme(CatalogLoader.LoadLocalSettings().Theme != "light");

        new MainWindow().Show();
    }

    public static void ApplyTheme(bool dark) => Current.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
}
