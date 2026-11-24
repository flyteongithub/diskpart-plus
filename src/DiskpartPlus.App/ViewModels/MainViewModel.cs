using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Input;
using System.Windows;
using DiskpartPlus.App.Mvvm;
using DiskpartPlus.App.Services;
using DiskpartPlus.App.Views;
using DiskpartPlus.Core.Execution;
using DiskpartPlus.Core.Models;
using DiskpartPlus.Core.Operations;
using DiskpartPlus.Core.Planning;

namespace DiskpartPlus.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    public const string ProjectUrl = "https://github.com/flyteongithub/diskpart-plus";

    private readonly IStorageInventory _inventory;
    private readonly IDiskpartRunner _runner;
    private readonly Func<Window> _owner;
    private readonly StringBuilder _log = new();

    private StorageSnapshot? _snapshot;
    private DiskViewModel? _selectedDisk;
    private PartitionViewModel? _selectedPartition;
    private SegmentViewModel? _selectedSegment;
    private string _status = "Reading disks...";
    private bool _isBusy;

    public MainViewModel(IStorageInventory inventory, IDiskpartRunner runner, Func<Window> owner)
    {
        _inventory = inventory;
        _runner = runner;
        _owner = owner;

        RefreshCommand = new AsyncCommand(RefreshAsync, () => !IsBusy);
        AboutCommand = new RelayCommand(ShowAbout);

        WipeAndReuseCommand = DiskCommand((s, d) => DiskActions.WipeAndReuse(_owner(), s, d));
        CreatePartitionCommand = DiskCommand((s, d) => DiskActions.CreatePartition(_owner(), s, d));
        ConvertCommand = DiskCommand((_, d) => DiskActions.ConvertStyle(_owner(), d));
        CleanCommand = DiskCommand((_, d) => [new CleanDisk(d.Number)]);
        CleanAllCommand = DiskCommand((_, d) => [new CleanDisk(d.Number, ZeroEverySector: true)]);
        ToggleOnlineCommand = DiskCommand((_, d) => [new SetDiskOnline(d.Number, d.IsOffline)]);
        ToggleReadOnlyCommand = DiskCommand((_, d) => [new SetDiskReadOnly(d.Number, !d.IsReadOnly)]);
        ChangeDiskIdCommand = DiskCommand((_, d) => DiskActions.ChangeDiskId(_owner(), d));
        DetachCommand = DiskCommand((_, d) => DiskActions.DetachVirtualDisk(d), d => d.IsVirtual);

        FormatCommand = PartitionCommand((_, _, p) => DiskActions.Format(_owner(), p));
        DeleteCommand = PartitionCommand((_, _, p) => DiskActions.Delete(p));
        ChangeLetterCommand = PartitionCommand((s, _, p) => DiskActions.ChangeLetter(_owner(), s, p));
        RemoveLetterCommand = PartitionCommand((_, _, p) => DiskActions.RemoveLetter(p), p => p.Letter is not null);
        ExtendCommand = PartitionCommand((_, _, p) => DiskActions.Extend(_owner(), p));
        ShrinkCommand = PartitionCommand((_, _, p) => DiskActions.Shrink(_owner(), p));
        ChangeTypeCommand = PartitionCommand((_, d, p) => DiskActions.ChangeType(_owner(), d, p));
        GptAttributesCommand = PartitionCommand((_, _, p) => DiskActions.GptAttributes(_owner(), p), _ => SelectedDisk?.Disk.Style == PartitionStyle.Gpt);

        CreateVhdCommand = new AsyncCommand(() => ReviewAndRunAsync(_snapshot is null ? null : DiskActions.CreateVirtualDisk(_owner(), _snapshot)), () => !IsBusy && _snapshot is not null);
        AttachVhdCommand = new AsyncCommand(() => ReviewAndRunAsync(DiskActions.AttachVirtualDisk(_owner())), () => !IsBusy && _snapshot is not null);
    }

    public ObservableCollection<DiskViewModel> Disks { get; } = [];

    public DiskViewModel? SelectedDisk
    {
        get => _selectedDisk;
        set
        {
            if (SetProperty(ref _selectedDisk, value))
            {
                SelectedPartition = null;
                OnPropertyChanged(nameof(HasDiskSelected));
                OnPropertyChanged(nameof(NoDiskSelected));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public PartitionViewModel? SelectedPartition
    {
        get => _selectedPartition;
        set
        {
            if (SetProperty(ref _selectedPartition, value))
            {
                // Keep the bar and the table pointing at the same partition.
                SelectedSegment = value is null ? null : SelectedDisk?.Segments.FirstOrDefault(s => s.Partition == value);
                OnPropertyChanged(nameof(HasPartitionSelected));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public SegmentViewModel? SelectedSegment
    {
        get => _selectedSegment;
        set
        {
            if (SetProperty(ref _selectedSegment, value) && value?.Partition != _selectedPartition)
            {
                SelectedPartition = value?.Partition;
            }
        }
    }

    public bool HasDiskSelected => SelectedDisk is not null;

    public bool NoDiskSelected => SelectedDisk is null;

    public bool HasPartitionSelected => SelectedPartition is not null;

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string LogText => _log.ToString();

    public string VersionText
    {
        get
        {
            var version = typeof(MainViewModel).Assembly.GetName().Version;
            return version is null ? string.Empty : $"v{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    public ICommand RefreshCommand { get; }
    public ICommand AboutCommand { get; }
    public ICommand WipeAndReuseCommand { get; }
    public ICommand CreatePartitionCommand { get; }
    public ICommand ConvertCommand { get; }
    public ICommand CleanCommand { get; }
    public ICommand CleanAllCommand { get; }
    public ICommand ToggleOnlineCommand { get; }
    public ICommand ToggleReadOnlyCommand { get; }
    public ICommand ChangeDiskIdCommand { get; }
    public ICommand DetachCommand { get; }
    public ICommand FormatCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand ChangeLetterCommand { get; }
    public ICommand RemoveLetterCommand { get; }
    public ICommand ExtendCommand { get; }
    public ICommand ShrinkCommand { get; }
    public ICommand ChangeTypeCommand { get; }
    public ICommand GptAttributesCommand { get; }
    public ICommand CreateVhdCommand { get; }
    public ICommand AttachVhdCommand { get; }

    public async Task RefreshAsync()
    {
        IsBusy = true;
        Status = "Reading disks...";

        var keepDisk = SelectedDisk?.Number;
        var keepPartition = SelectedPartition?.Number;

        try
        {
            _snapshot = await _inventory.GetSnapshotAsync();

            Disks.Clear();
            foreach (var disk in _snapshot.Disks)
            {
                Disks.Add(new DiskViewModel(disk));
            }

            SelectedDisk = Disks.FirstOrDefault(d => d.Number == keepDisk)
                ?? Disks.FirstOrDefault(d => !d.Disk.IsCritical)
                ?? Disks.FirstOrDefault();

            if (keepPartition is { } number && SelectedDisk?.Number == keepDisk)
            {
                SelectedPartition = SelectedDisk?.Partitions.FirstOrDefault(p => p.Number == number);
            }

            Status = $"{Disks.Count} disk{(Disks.Count == 1 ? string.Empty : "s")} found. Last checked {DateTime.Now:HH:mm:ss}.";
        }
        catch (Exception ex)
        {
            Status = "Couldn't read the disks.";
            MessageBox.Show($"Couldn't read the list of disks:\n\n{ex.Message}", "Diskpart+", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private AsyncCommand DiskCommand(Func<StorageSnapshot, DiskInfo, IReadOnlyList<DiskOperation>?> build, Func<DiskInfo, bool>? extra = null) =>
        new(
            () => SelectedDisk is { } disk && _snapshot is { } snapshot ? ReviewAndRunAsync(build(snapshot, disk.Disk)) : Task.CompletedTask,
            () => !IsBusy && _snapshot is not null && SelectedDisk is { } d && (extra?.Invoke(d.Disk) ?? true));

    private AsyncCommand PartitionCommand(Func<StorageSnapshot, DiskInfo, PartitionInfo, IReadOnlyList<DiskOperation>?> build, Func<PartitionInfo, bool>? extra = null) =>
        new(
            () => SelectedDisk is { } disk && SelectedPartition is { } partition && _snapshot is { } snapshot
                ? ReviewAndRunAsync(build(snapshot, disk.Disk, partition.Partition))
                : Task.CompletedTask,
            () => !IsBusy && _snapshot is not null && SelectedPartition is { } p && (extra?.Invoke(p.Partition) ?? true));

    private async Task ReviewAndRunAsync(IReadOnlyList<DiskOperation>? operations)
    {
        if (operations is null || operations.Count == 0 || _snapshot is null)
        {
            return;
        }

        var plan = OperationPlanner.Create(operations, new PlanningContext(_snapshot) { FileExists = File.Exists });
        var window = new PlanWindow(plan, _runner, _inventory) { Owner = _owner() };
        window.ShowDialog();

        if (window.Output.Count > 0)
        {
            _log.AppendLine($"--- {DateTime.Now:HH:mm:ss} ---");
            foreach (var line in window.Output)
            {
                _log.AppendLine(line);
            }

            OnPropertyChanged(nameof(LogText));
        }

        if (window.Ran)
        {
            await RefreshAsync();
        }
    }

    private void ShowAbout()
    {
        var answer = MessageBox.Show(
            $"Diskpart+ {VersionText}\n\nA modern, safe GUI for Windows diskpart.\nMade by flyte.\n\nOpen source under the MIT licence. Not affiliated with Microsoft.\n\nOpen the project page on GitHub?",
            "About Diskpart+",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);

        if (answer == MessageBoxResult.Yes)
        {
            Process.Start(new ProcessStartInfo(ProjectUrl) { UseShellExecute = true });
        }
    }
}
