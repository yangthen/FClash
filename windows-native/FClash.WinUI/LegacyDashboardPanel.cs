using FClash.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace FClash.WinUI;

internal sealed class LegacyDashboardPanel : Panel
{
    public bool EnableReorder { get; set; }
    public event Action<IEnumerable<string>>? Reordered;
    private FrameworkElement? dragged;
    private Point origin;
    public LegacyDashboardPanel()
    {
        PointerPressed += (_, e) =>
        {
            if (!EnableReorder || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            for (var node = e.OriginalSource as DependencyObject; node != null && node != this; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node)) if (node is Button) return;
            origin = e.GetCurrentPoint(this).Position;
            var index = layout.ToList().FindIndex(x => new Rect(x.X, x.Y, x.Width, x.Height).Contains(origin));
            if (index < 0) return;
            dragged = (FrameworkElement)Children[index]; CapturePointer(e.Pointer); e.Handled = true;
        };
        PointerMoved += (_, e) =>
        {
            if (dragged == null) return; var at = e.GetCurrentPoint(this).Position;
            dragged.Translation = new System.Numerics.Vector3((float)(at.X - origin.X), (float)(at.Y - origin.Y), 2);
        };
        PointerReleased += (_, e) =>
        {
            if (dragged == null) return;
            var at = e.GetCurrentPoint(this).Position; var from = Children.IndexOf(dragged);
            var to = layout.ToList().FindIndex(x => new Rect(x.X, x.Y, x.Width, x.Height).Contains(at));
            dragged.Translation = default;
            if (to >= 0 && from != to) { Children.Remove(dragged); Children.Insert(to, dragged); Reordered?.Invoke(Children.Cast<FrameworkElement>().Select(x => (string)x.Tag).ToArray()); }
            dragged = null; ReleasePointerCaptures(); e.Handled = true;
        };
        PointerCaptureLost += (_, _) => { if (dragged != null) dragged.Translation = default; dragged = null; };
    }
    private IReadOnlyList<LegacyUiLayout.Tile> layout = [];
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 600;
        layout = LegacyUiLayout.Dashboard(width + 32, Children.Cast<FrameworkElement>().Select(x => (string)x.Tag));
        for (var i = 0; i < Children.Count; i++) Children[i].Measure(new Size(layout[i].Width, layout[i].Height));
        return new Size(width, layout.Count == 0 ? 0 : layout.Max(x => x.Y + x.Height));
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        for (var i = 0; i < Children.Count && i < layout.Count; i++)
        { var tile = layout[i]; Children[i].Arrange(new Rect(tile.X, tile.Y, tile.Width, tile.Height)); }
        return finalSize;
    }
}
