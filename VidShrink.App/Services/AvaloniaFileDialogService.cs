using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace VidShrink.App.Services;

public class AvaloniaFileDialogService : IFileDialogService
{
    private IStorageProvider? GetStorageProvider()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow?.StorageProvider;
        }
        return null;
    }

    public async Task<IReadOnlyList<string>> OpenVideoFilesAsync()
    {
        var sp = GetStorageProvider();
        if (sp == null) return Array.Empty<string>();

        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Выберите видеофайлы для сжатия",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Видеофайлы")
                {
                    Patterns = new[] { "*.mp4", "*.mkv", "*.mov", "*.avi", "*.webm", "*.flv", "*.wmv", "*.m4v", "*.ts" }
                },
                new FilePickerFileType("Все файлы")
                {
                    Patterns = new[] { "*.*" }
                }
            }
        });

        return files
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrEmpty(p))
            .Cast<string>()
            .ToList();
    }

    public async Task<string?> OpenFfmpegExecutableAsync()
    {
        var sp = GetStorageProvider();
        if (sp == null) return null;

        var isWindows = OperatingSystem.IsWindows();
        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Укажите исполняемый файл FFmpeg",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Исполняемые файлы FFmpeg")
                {
                    Patterns = isWindows ? new[] { "ffmpeg.exe", "*.exe" } : new[] { "ffmpeg", "*" }
                },
                new FilePickerFileType("Все файлы")
                {
                    Patterns = new[] { "*.*" }
                }
            }
        });

        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    public async Task<string?> OpenFolderAsync(string? initialDirectory = null)
    {
        var sp = GetStorageProvider();
        if (sp == null) return null;

        var options = new FolderPickerOpenOptions
        {
            Title = "Выберите папку для сохранения",
            AllowMultiple = false
        };

        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            try
            {
                var folder = await sp.TryGetFolderFromPathAsync(initialDirectory);
                if (folder != null)
                {
                    options.SuggestedStartLocation = folder;
                }
            }
            catch
            {
                // Fallback to default start location
            }
        }

        var folders = await sp.OpenFolderPickerAsync(options);
        return folders.FirstOrDefault()?.TryGetLocalPath();
    }
}
