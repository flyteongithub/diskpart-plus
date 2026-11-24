using System.IO;
using System.Windows.Controls;
using System.Windows;
using Microsoft.Win32;

namespace DiskpartPlus.App.Views;

/// <summary>
/// A small "fill in a few fields" dialog built in code. Each action adds the fields it needs,
/// so we don't need a separate XAML file for every little form.
/// </summary>
public sealed class FormDialog : Window
{
    private readonly StackPanel _fields = new();
    private readonly TextBlock _error = new() { Foreground = Palette.Danger, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed };

    public FormDialog(Window? owner, string title, string intro, string continueText = "Continue")
    {
        Title = title;
        Owner = owner;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var ok = new Button { Content = continueText, IsDefault = true, MinWidth = 104, Margin = new Thickness(8, 0, 0, 0) };
        ok.Click += (_, _) => TryAccept();
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 104 };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("HeaderText"), Margin = new Thickness(0, 0, 0, 6) });
        root.Children.Add(new TextBlock { Text = intro, Style = (Style)FindResource("CaptionText"), Margin = new Thickness(0, 0, 0, 12) });
        root.Children.Add(_fields);
        root.Children.Add(_error);
        root.Children.Add(buttons);
        Content = root;
    }

    /// <summary>Returns an error message to show, or null if the form is good to go.</summary>
    public Func<string?>? Validate { get; set; }

    public bool Ask() => ShowDialog() == true;

    public TextBox AddText(string label, string value, string? help = null)
    {
        var box = new TextBox { Text = value };
        AddRow(label, box, help);
        return box;
    }

    public ComboBox AddChoice<T>(string label, IEnumerable<(string Text, T Value)> items, int selectedIndex = 0, string? help = null)
    {
        var box = new ComboBox();
        foreach (var (text, value) in items)
        {
            box.Items.Add(new ComboBoxItem { Content = text, Tag = value });
        }

        box.SelectedIndex = box.Items.Count == 0 ? -1 : Math.Clamp(selectedIndex, 0, box.Items.Count - 1);
        AddRow(label, box, help);
        return box;
    }

    public CheckBox AddCheck(string text, bool isChecked, string? help = null)
    {
        var box = new CheckBox { Content = text, IsChecked = isChecked };
        AddRow(null, box, help);
        return box;
    }

    public TextBox AddPath(string label, string value, bool save, string filter, string defaultExtension)
    {
        var box = new TextBox { Text = value };
        var browse = new Button { Content = "Browse...", Margin = new Thickness(8, 0, 0, 0), MinWidth = 88 };
        browse.Click += (_, _) =>
        {
            FileDialog dialog = save
                ? new SaveFileDialog { Filter = filter, DefaultExt = defaultExtension, OverwritePrompt = false, FileName = Path.GetFileName(box.Text) }
                : new OpenFileDialog { Filter = filter, DefaultExt = defaultExtension, CheckFileExists = true };

            if (dialog.ShowDialog(this) == true)
            {
                box.Text = dialog.FileName;
            }
        };

        var row = new DockPanel();
        DockPanel.SetDock(browse, Dock.Right);
        row.Children.Add(browse);
        row.Children.Add(box);
        AddRow(label, row, null);
        return box;
    }

    // Choices like "Don't format it" carry a null value, so null is a real answer here.
    public static T ValueOf<T>(ComboBox box)
    {
        if (box.SelectedItem is not ComboBoxItem item)
        {
            throw new InvalidOperationException("Nothing is selected.");
        }

        return item.Tag is null ? default! : (T)item.Tag;
    }

    private void AddRow(string? label, FrameworkElement control, string? help)
    {
        var row = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        if (label is not null)
        {
            row.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) });
        }

        row.Children.Add(control);

        if (help is not null)
        {
            row.Children.Add(new TextBlock { Text = help, Style = (Style)FindResource("CaptionText"), Margin = new Thickness(0, 4, 0, 0) });
        }

        _fields.Children.Add(row);
    }

    private void TryAccept()
    {
        var problem = Validate?.Invoke();
        if (problem is not null)
        {
            _error.Text = problem;
            _error.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }
}
