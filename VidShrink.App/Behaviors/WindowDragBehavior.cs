using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace VidShrink.App.Behaviors;

public static class WindowDragBehavior
{
    public static readonly AttachedProperty<bool> EnableDragProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>(
            "EnableDrag",
            typeof(WindowDragBehavior),
            false);

    public static bool GetEnableDrag(Control element) => element.GetValue(EnableDragProperty);
    public static void SetEnableDrag(Control element, bool value) => element.SetValue(EnableDragProperty, value);

    static WindowDragBehavior()
    {
        EnableDragProperty.Changed.AddClassHandler<Control>((control, args) =>
        {
            if (args.NewValue is true)
            {
                control.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Bubble);
            }
            else
            {
                control.RemoveHandler(InputElement.PointerPressedEvent, OnPointerPressed);
            }
        });
    }

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Handled) return;

        if (e.Source is Visual visual)
        {
            var parent = visual;
            while (parent != null && parent != sender)
            {
                if (parent is Button or TextBox or ComboBox or ListBoxItem)
                {
                    return;
                }
                parent = parent.GetVisualParent();
            }
        }

        if (sender is Control control && TopLevel.GetTopLevel(control) is Window window)
        {
            if (e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
            {
                if (e.ClickCount == 2 && window.CanResize)
                {
                    window.WindowState = window.WindowState == WindowState.Maximized
                        ? WindowState.Normal
                        : WindowState.Maximized;
                    e.Handled = true;
                }
                else
                {
                    window.BeginMoveDrag(e);
                    e.Handled = true;
                }
            }
        }
    }
}
