using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DiskpartPlus.App.Views;
using DiskpartPlus.Core.Planning;
using DiskpartPlus.Core.Templates;
using DiskpartPlus.Windows.Execution;
using DiskpartPlus.Windows.Inventory;

namespace DiskpartPlus.App;

/// <summary>
/// "DiskpartPlus.exe --render-to folder" opens the main window and a review window, saves
/// pictures of them in light and dark, then quits. CI uses this to check the UI actually loads.
/// It never runs diskpart.
/// </summary>
internal static class UiSnapshot
{
    public const string Flag = "--render-to";

    public static async Task RunAsync(Application app, string folder)
    {
        Directory.CreateDirectory(folder);

        try
        {
            var main = new MainWindow();
            app.MainWindow = main;
            main.Show();
            await Settle(main);
            await main.ViewModel.RefreshAsync();

            // Show something more interesting than the Windows disk if there is one.
            var vm = main.ViewModel;
            vm.SelectedDisk = vm.Disks.FirstOrDefault(d => d.Disk.HasApplePartitions)
                ?? vm.Disks.FirstOrDefault(d => !d.Disk.IsCritical)
                ?? vm.Disks.FirstOrDefault();
            vm.SelectedPartition = vm.SelectedDisk?.Partitions.LastOrDefault();
            await Settle(main);
            await SaveAt(main, 1240, 800, Path.Combine(folder, "main-light.png"));

            if (vm.SelectedDisk is { } disk)
            {
                var snapshot = await new CimStorageInventory().GetSnapshotAsync();
                var plan = OperationPlanner.Create(
                    WipeAndReuse.Build(disk.Number),
                    new PlanningContext(snapshot) { FileExists = File.Exists, ScriptCanHold = DiskpartRunner.CanHold });

                var review = new PlanWindow(plan, new DiskpartRunner(), new CimStorageInventory()) { Owner = main };
                review.Show();
                await Settle(review);
                await SaveAt(review, 760, 640, Path.Combine(folder, "review-light.png"));
                review.Close();
            }

            app.ThemeMode = ThemeMode.Dark;
            await Settle(main);
            await SaveAt(main, 1240, 800, Path.Combine(folder, "main-dark.png"));

            app.Shutdown(0);
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "error.txt"), ex.ToString());
            app.Shutdown(2);
        }
    }

    // Give WPF a moment to finish layout, data binding and rendering.
    private static async Task Settle(Window window)
    {
        await Task.Delay(400);
        await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.ApplicationIdle);
    }

    // Windows won't let a window grow past the screen, and CI machines have small screens.
    // So lift the window's content out, lay it out at a normal desktop size, take the
    // picture, then put it back.
    private static async Task SaveAt(Window window, double width, double height, string path)
    {
        if (window.Content is not FrameworkElement content)
        {
            return;
        }

        var dataContext = window.DataContext;
        window.Content = null;
        content.DataContext = dataContext;

        try
        {
            for (var pass = 0; pass < 2; pass++)
            {
                content.Measure(new Size(width, height));
                content.Arrange(new Rect(0, 0, width, height));
                await Task.Delay(300);
                await window.Dispatcher.InvokeAsync(content.UpdateLayout, DispatcherPriority.ApplicationIdle);
            }

            Save(window, content, width, height, path);
        }
        finally
        {
            window.Content = content;
        }
    }

    private static void Save(Window window, FrameworkElement content, double width, double height, string path)
    {
        // Render at 1.5x so the pictures look sharp on high-DPI screens.
        const double scale = 1.5;

        // Paint a solid background first. The real window uses Mica, which a bitmap can't capture.
        var background = window.Background is SolidColorBrush { Color.A: 255 } solid
            ? solid
            : new SolidColorBrush(Application.Current.ThemeMode == ThemeMode.Dark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3));

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(background, null, new Rect(0, 0, width, height));
            context.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, width, height));
        }

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(width * scale),
            (int)Math.Ceiling(height * scale),
            96 * scale,
            96 * scale,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
