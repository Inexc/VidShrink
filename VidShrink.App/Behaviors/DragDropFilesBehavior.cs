using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace VidShrink.App.Behaviors;

public static class DragDropFilesBehavior
{
    public static readonly AttachedProperty<ICommand?> DropFilesCommandProperty =
        AvaloniaProperty.RegisterAttached<Control, ICommand?>(
            "DropFilesCommand",
            typeof(DragDropFilesBehavior));

    public static ICommand? GetDropFilesCommand(Control element) => element.GetValue(DropFilesCommandProperty);
    public static void SetDropFilesCommand(Control element, ICommand? value) => element.SetValue(DropFilesCommandProperty, value);

    static DragDropFilesBehavior()
    {
        DropFilesCommandProperty.Changed.AddClassHandler<Control>((control, args) =>
        {
            if (args.NewValue is ICommand)
            {
                DragDrop.SetAllowDrop(control, true);
                control.AddHandler(DragDrop.DragOverEvent, OnDragOver);
                control.AddHandler(DragDrop.DropEvent, OnDrop);
            }
            else
            {
                control.RemoveHandler(DragDrop.DragOverEvent, OnDragOver);
                control.RemoveHandler(DragDrop.DropEvent, OnDrop);
            }
        });
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files))
        {
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private static void OnDrop(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains(DataFormats.Files))
            return;

        var files = e.Data.GetFiles();
        if (files == null) return;

        var paths = new List<string>();
        foreach (var file in files)
        {
            var local = file.TryGetLocalPath();
            if (string.IsNullOrEmpty(local) && file.Path != null && file.Path.IsAbsoluteUri && file.Path.IsFile)
            {
                local = file.Path.LocalPath;
            }

            if (!string.IsNullOrEmpty(local))
            {
                paths.Add(local);
            }
        }

        if (paths.Count > 0 && sender is Control control)
        {
            var cmd = GetDropFilesCommand(control);
            if (cmd != null && cmd.CanExecute(paths))
            {
                cmd.Execute(paths);
            }
        }
    }
}
