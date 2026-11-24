using System.Windows;
using System.Windows.Threading;
using DiskpartPlus.App.Views;
using DiskpartPlus.Windows;

namespace DiskpartPlus.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        // The manifest already asks for admin, so this only trips if someone works around it.
        if (!Elevation.IsAdministrator())
        {
            MessageBox.Show(
                "Diskpart+ needs to run as administrator because diskpart does.\n\nRight-click it and choose \"Run as administrator\".",
                "Diskpart+",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            Shutdown(1);
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Something went wrong:\n\n{e.Exception.Message}\n\nNothing was sent to diskpart because of this error.",
            "Diskpart+",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
