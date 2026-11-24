using System.Windows;
using DiskpartPlus.App.ViewModels;
using DiskpartPlus.Windows.Execution;
using DiskpartPlus.Windows.Inventory;

namespace DiskpartPlus.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainViewModel ViewModel => _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        // Don't open bigger than the screen on small laptops.
        var area = SystemParameters.WorkArea;
        Width = Math.Min(Width, area.Width * 0.94);
        Height = Math.Min(Height, area.Height * 0.94);
        _viewModel = new MainViewModel(new CimStorageInventory(), new DiskpartRunner(), () => this);
        DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.RefreshAsync();
    }
}
