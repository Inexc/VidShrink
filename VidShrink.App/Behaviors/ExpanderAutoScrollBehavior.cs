using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace VidShrink.App.Behaviors;

public static class ExpanderAutoScrollBehavior
{
    public static readonly AttachedProperty<bool> EnableAutoScrollProperty =
        AvaloniaProperty.RegisterAttached<Expander, bool>(
            "EnableAutoScroll",
            typeof(ExpanderAutoScrollBehavior),
            false);

    public static bool GetEnableAutoScroll(Expander element) => element.GetValue(EnableAutoScrollProperty);
    public static void SetEnableAutoScroll(Expander element, bool value) => element.SetValue(EnableAutoScrollProperty, value);

    static ExpanderAutoScrollBehavior()
    {
        Expander.IsExpandedProperty.Changed.AddClassHandler<Expander>((expander, args) =>
        {
            if (GetEnableAutoScroll(expander) && args.NewValue is true)
            {
                OnExpanded(expander);
            }
        });
    }

    private static void OnExpanded(Expander expander)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            // Give layout pass a short moment to begin unfolding and computing new bounds
            await Task.Delay(40);

            var scrollViewer = expander.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
            if (scrollViewer == null) return;

            AnimateScrollToBottom(scrollViewer, expander, TimeSpan.FromMilliseconds(320));
        }, DispatcherPriority.Loaded);
    }

    private static void AnimateScrollToBottom(ScrollViewer scrollViewer, Expander expander, TimeSpan duration)
    {
        var sw = Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        var startY = scrollViewer.Offset.Y;

        timer.Tick += (s, e) =>
        {
            var elapsed = sw.Elapsed.TotalMilliseconds;
            var total = duration.TotalMilliseconds;
            var t = Math.Clamp(elapsed / total, 0.0, 1.0);

            // Recompute target bounds dynamically as expander slides open
            var maxOffsetY = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
            var transform = expander.TransformToVisual(scrollViewer);
            double targetOffsetY = maxOffsetY;
            if (transform != null)
            {
                var expanderTopInScroll = transform.Value.Transform(new Point(0, 0)).Y + scrollViewer.Offset.Y;
                var expanderBottomInScroll = expanderTopInScroll + expander.Bounds.Height;
                targetOffsetY = Math.Min(maxOffsetY, expanderBottomInScroll - scrollViewer.Viewport.Height + 16);
            }
            targetOffsetY = Math.Max(0, Math.Min(maxOffsetY, targetOffsetY));

            if (targetOffsetY <= startY + 2)
            {
                timer.Stop();
                return;
            }

            // Smooth cubic ease out
            var ease = 1.0 - Math.Pow(1.0 - t, 3);
            var currentY = startY + (targetOffsetY - startY) * ease;

            scrollViewer.Offset = new Vector(scrollViewer.Offset.X, currentY);

            if (t >= 1.0)
            {
                timer.Stop();
                scrollViewer.Offset = new Vector(scrollViewer.Offset.X, targetOffsetY);
            }
        };
        timer.Start();
    }
}
