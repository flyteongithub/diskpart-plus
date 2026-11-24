using System.Windows;
using DiskpartPlus.App.ViewModels;
using DiskpartPlus.Windows.Execution;
using DiskpartPlus.Windows.Inventory;

namespace DiskpartPlus.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(new CimStorageInventory(), new DiskpartRunner(), () => this);
        DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.RefreshAsync();
    }
}
