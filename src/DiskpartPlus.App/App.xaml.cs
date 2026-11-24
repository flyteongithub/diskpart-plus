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
                "Diskpart+ needs to run as administrator.",
                "Diskpart+",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            Shutdown(1);
            return;
        }

        if (e.Args is [UiSnapshot.Flag, var folder, ..])
        {
            _ = UiSnapshot.RunAsync(this, folder);
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            e.Exception.Message,
            "Diskpart+",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
