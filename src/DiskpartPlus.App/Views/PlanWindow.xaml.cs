using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DiskpartPlus.Core.Execution;
using DiskpartPlus.Core.Operations;
using DiskpartPlus.Core.Planning;
using DiskpartPlus.Core.Safety;

namespace DiskpartPlus.App.Views;

/// <summary>
/// The one check before anything that erases data: what will happen, the script, and a button
/// that says exactly what it does. Then it runs and shows diskpart's output.
/// </summary>
public partial class PlanWindow : Window
{
    private readonly OperationPlan _plan;
    private readonly IDiskpartRunner _runner;
    private readonly IStorageInventory _inventory;
    private readonly List<string> _output = [];
    private CancellationTokenSource? _stop;
    private bool _running;

    public PlanWindow(OperationPlan plan, IDiskpartRunner runner, IStorageInventory inventory, string? diskName = null)
    {
        InitializeComponent();
        _plan = plan;
        _runner = runner;
        _inventory = inventory;
        ShowPlan(diskName);
    }

    /// <summary>True once diskpart has actually been started, so the disk list needs refreshing.</summary>
    public bool Ran { get; private set; }

    public IReadOnlyList<string> Output => _output;

    public sealed record IssueRow(string Message, Brush Brush);

    private void ShowPlan(string? diskName)
    {
        var verb = _plan.Risk == RiskLevel.Critical ? "Wipe" : "Erase";
        var action = _plan.ErasedDisk is { } disk ? $"{verb} disk {disk}" : "Run";

        HeadingText.Text = _plan.CanRun ? action + "?" : "Can't run this";
        SubtitleText.Text = diskName ?? string.Empty;
        SubtitleText.Visibility = string.IsNullOrEmpty(diskName) ? Visibility.Collapsed : Visibility.Visible;

        StepsList.ItemsSource = _plan.Operations.Select(o => o.Describe()).ToList();
        IssuesList.ItemsSource = _plan.Issues.Select(i => new IssueRow(i.Message, Palette.ForSeverity(i.Severity))).ToList();
        ScriptBox.Text = _plan.Script?.ToText() ?? string.Empty;

        RunButton.Content = action;
        RunButton.IsEnabled = _plan.CanRun;
        if (_plan.NeedsReview)
        {
            RunButton.Background = Palette.Danger;
            RunButton.Foreground = Palette.White;
        }
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (!_plan.CanRun || _plan.Script is null || _running)
        {
            return;
        }

        _running = true;
        RunButton.IsEnabled = false;
        ReviewPanel.Visibility = Visibility.Collapsed;
        RunPanel.Visibility = Visibility.Visible;
        CancelButton.IsEnabled = false;

        // Stopping halfway is only reasonable for "clean all": the partitions are already gone by then.
        if (_plan.Operations.Any(o => o is CleanDisk { ZeroEverySector: true }))
        {
            _stop = new CancellationTokenSource();
            StopButton.Visibility = Visibility.Visible;
        }

        try
        {
            RunStatus.Text = "Running...";
            var problems = _plan.CheckTargetsStillMatch(await _inventory.GetSnapshotAsync());
            if (problems.Count > 0)
            {
                foreach (var problem in problems)
                {
                    Append(problem);
                }

                Finish(false, "Nothing ran. The disks changed.");
                return;
            }

            foreach (var line in _plan.Script.Commands)
            {
                Append("> " + line);
            }

            Ran = true;
            var result = await _runner.RunAsync(_plan.Script, new Progress<string>(Append), _stop?.Token ?? CancellationToken.None);

            // Let any output lines still queued up land before the final message.
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);

            Append($"Exit code {result.CodeText}");
            Finish(result.Succeeded, result.Succeeded ? "Done." : result.Explain());
        }
        catch (OperationCanceledException)
        {
            Finish(false, "Stopped. Partitions are gone, the rest wasn't zeroed.");
        }
        catch (Exception ex)
        {
            Append(ex.ToString());
            Finish(false, ex.Message);
        }
    }

    private void Finish(bool succeeded, string message)
    {
        _running = false;
        RunStatus.Text = message;
        RunStatus.Foreground = succeeded ? Palette.Good : Palette.Danger;
        RunProgress.IsIndeterminate = false;
        RunProgress.Value = RunProgress.Maximum;
        StopButton.Visibility = Visibility.Collapsed;
        RunButton.Visibility = Visibility.Collapsed;
        CancelButton.Content = "Close";
        CancelButton.IsEnabled = true;
    }

    private void Append(string line)
    {
        _output.Add(line);
        OutputBox.AppendText(line + Environment.NewLine);
        OutputBox.ScrollToEnd();
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => _stop?.Cancel();

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        // Don't let the window close while diskpart is mid-way through something.
        if (_running)
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }
}
