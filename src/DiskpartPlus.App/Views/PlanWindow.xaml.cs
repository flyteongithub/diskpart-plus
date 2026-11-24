using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DiskpartPlus.Core.Execution;
using DiskpartPlus.Core.Operations;
using DiskpartPlus.Core.Planning;
using DiskpartPlus.Core.Safety;

namespace DiskpartPlus.App.Views;

/// <summary>
/// Shows a plan before it runs (steps, warnings, the exact script and the typed confirmation),
/// then runs it and shows diskpart's output live.
/// </summary>
public partial class PlanWindow : Window
{
    private readonly OperationPlan _plan;
    private readonly IDiskpartRunner _runner;
    private readonly IStorageInventory _inventory;
    private readonly List<string> _output = [];
    private CancellationTokenSource? _stop;
    private bool _running;

    public PlanWindow(OperationPlan plan, IDiskpartRunner runner, IStorageInventory inventory)
    {
        InitializeComponent();
        _plan = plan;
        _runner = runner;
        _inventory = inventory;
        ShowPlan();
    }

    /// <summary>True once diskpart has actually been started, so the disk list needs refreshing.</summary>
    public bool Ran { get; private set; }

    public IReadOnlyList<string> Output => _output;

    public sealed record IssueRow(string Kind, string Message, Brush Brush);

    private void ShowPlan()
    {
        RiskText.Text = _plan.Risk switch
        {
            RiskLevel.Safe => "SAFE",
            RiskLevel.Caution => "CAUTION",
            RiskLevel.Destructive => "ERASES DATA",
            _ => "ERASES EVERYTHING",
        };
        RiskPill.Background = Palette.ForRisk(_plan.Risk);

        HeadingText.Text = _plan.CanRun ? "Review changes" : "This can't run yet";
        SummaryText.Text = _plan.CanRun
            ? "Nothing has changed yet. Check the steps below, then run them."
            : "Diskpart+ stopped this before it reached diskpart. The red items below say why.";

        StepsList.ItemsSource = _plan.Operations.Select((o, i) => $"{i + 1}.  {o.Describe()}").ToList();

        IssuesList.ItemsSource = _plan.Issues.Select(i => new IssueRow(
            i.Severity switch
            {
                IssueSeverity.Blocker => "Blocked",
                IssueSeverity.Warning => "Warning",
                _ => "Note",
            },
            i.Message,
            Palette.ForSeverity(i.Severity))).ToList();
        IssuesHeader.Visibility = _plan.Issues.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        ScriptBox.Text = _plan.Script?.ToText() ?? "No script could be built for this.";

        if (_plan.CanRun && _plan.ConfirmationPhrase is { } phrase)
        {
            ConfirmLabel.Text = $"This erases data and can't be undone. Type {phrase} to confirm.";
            ConfirmPanel.Visibility = Visibility.Visible;
        }
        else
        {
            ConfirmPanel.Visibility = Visibility.Collapsed;
        }

        UpdateRunButton();
    }

    private void UpdateRunButton() =>
        RunButton.IsEnabled = !_running && _plan.CanRun && _plan.IsConfirmedBy(ConfirmBox.Text);

    private void ConfirmBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateRunButton();

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        if (!_plan.CanRun || _plan.Script is null || !_plan.IsConfirmedBy(ConfirmBox.Text))
        {
            return;
        }

        _running = true;
        UpdateRunButton();
        ReviewPanel.Visibility = Visibility.Collapsed;
        ConfirmPanel.Visibility = Visibility.Collapsed;
        RunPanel.Visibility = Visibility.Visible;
        CancelButton.IsEnabled = false;

        // Stopping halfway is only safe-ish for "clean all": the partitions are already gone by then.
        if (_plan.Operations.Any(o => o is CleanDisk { ZeroEverySector: true }))
        {
            _stop = new CancellationTokenSource();
            StopButton.Visibility = Visibility.Visible;
        }

        var progress = new Progress<string>(Append);

        try
        {
            RunStatus.Text = "Checking the disks haven't changed since you looked...";
            var fresh = await _inventory.GetSnapshotAsync();
            var problems = _plan.CheckTargetsStillMatch(fresh);
            if (problems.Count > 0)
            {
                foreach (var problem in problems)
                {
                    Append(problem);
                }

                Finish(false, "Nothing was run because the disks changed. Close this and check the list again.");
                return;
            }

            RunStatus.Text = "Running diskpart...";
            Append("Script:");
            foreach (var line in _plan.Script.Lines)
            {
                Append("    " + line);
            }

            Append(string.Empty);
            Ran = true;

            var result = await _runner.RunAsync(_plan.Script, progress, _stop?.Token ?? CancellationToken.None);

            // Let any output lines still queued up land before the final message.
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);

            Append(string.Empty);
            Append($"Exit code {result.RawExitCode} after {result.Duration.TotalSeconds:0.0} seconds.");
            Finish(result.Succeeded, result.Succeeded ? "Done. " + result.Explain() : "diskpart stopped with an error. " + result.Explain());
        }
        catch (OperationCanceledException)
        {
            Finish(false, "Stopped. The disk has no partitions left but wasn't fully zeroed.");
        }
        catch (Exception ex)
        {
            Append(ex.ToString());
            Finish(false, "Something went wrong: " + ex.Message);
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

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            "Stop clean all now? The partitions are already gone, so the disk will be blank but not every sector will be zeroed.",
            "Stop clean all",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (answer == MessageBoxResult.Yes)
        {
            _stop?.Cancel();
        }
    }

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
