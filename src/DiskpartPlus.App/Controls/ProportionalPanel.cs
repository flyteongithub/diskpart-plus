using System.Windows;
using System.Windows.Controls;

namespace DiskpartPlus.App.Controls;

/// <summary>
/// Lays children out in a row with widths in proportion to their Weight, like the bar in
/// Disk Management. Every child gets a minimum width so tiny partitions are still clickable.
/// </summary>
public sealed class ProportionalPanel : Panel
{
    public static readonly DependencyProperty WeightProperty = DependencyProperty.RegisterAttached(
        "Weight",
        typeof(double),
        typeof(ProportionalPanel),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsParentArrange | FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static double GetWeight(UIElement element) => (double)element.GetValue(WeightProperty);

    public static void SetWeight(UIElement element, double value) => element.SetValue(WeightProperty, value);

    public double MinimumItemWidth { get; set; } = 64;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 800 : availableSize.Width;
        var widths = Widths(width);
        var height = 0.0;

        for (var i = 0; i < InternalChildren.Count; i++)
        {
            InternalChildren[i].Measure(new Size(widths[i], availableSize.Height));
            height = Math.Max(height, InternalChildren[i].DesiredSize.Height);
        }

        return new Size(width, double.IsInfinity(availableSize.Height) ? height : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var widths = Widths(finalSize.Width);
        var x = 0.0;

        for (var i = 0; i < InternalChildren.Count; i++)
        {
            InternalChildren[i].Arrange(new Rect(x, 0, widths[i], finalSize.Height));
            x += widths[i];
        }

        return finalSize;
    }

    private double[] Widths(double total)
    {
        var count = InternalChildren.Count;
        var widths = new double[count];
        if (count == 0)
        {
            return widths;
        }

        var weights = new double[count];
        for (var i = 0; i < count; i++)
        {
            weights[i] = Math.Max(0, GetWeight(InternalChildren[i]));
        }

        // Everyone gets the minimum first, then the rest is shared out by weight.
        var minimum = Math.Min(MinimumItemWidth, total / count);
        var rest = Math.Max(0, total - (minimum * count));
        var sum = weights.Sum();

        for (var i = 0; i < count; i++)
        {
            widths[i] = minimum + (sum > 0 ? rest * weights[i] / sum : rest / count);
        }

        return widths;
    }
}
