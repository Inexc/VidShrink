using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VidShrink.App.Services;
using VidShrink.Core.Models;
using VidShrink.Core.Platform;
using VidShrink.Core.Services;

namespace VidShrink.App.ViewModels;

public partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly IFfmpegLocator _locator;
    private readonly IFileDialogService _fileDialogService;
    private readonly IPlatformService _platformService;
    private CancellationTokenSource? _validationCts;

    public event Action? RequestClose;
    public event Action? SettingsSaved;

    [ObservableProperty]
    private string _ffmpegPath = string.Empty;

    [ObservableProperty]
    private string _defaultOutputDir = string.Empty;

    [ObservableProperty]
    private bool _preferHardware = true;

    [ObservableProperty]
    private string _ffmpegVersionText = string.Empty;

    [ObservableProperty]
    private bool _isFfmpegValid;

    public SettingsViewModel(
        ISettingsService? settingsService = null,
        IFfmpegLocator? locator = null,
        IFileDialogService? fileDialogService = null,
        IPlatformService? platformService = null)
    {
        _platformService = platformService ?? DefaultPlatformService.Instance;
        _settingsService = settingsService ?? new SettingsService(_platformService);
        _locator = locator ?? new FfmpegLocator(_platformService, _settingsService);
        _fileDialogService = fileDialogService ?? new AvaloniaFileDialogService();

        LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        var settings = _settingsService.CurrentSettings;
        FfmpegPath = settings.FfmpegPath ?? string.Empty;
        DefaultOutputDir = settings.DefaultOutputDir ?? string.Empty;
        PreferHardware = settings.PreferHardware;

        ValidateFfmpegPathSync(FfmpegPath);
    }

    partial void OnFfmpegPathChanged(string value)
    {
        _validationCts?.Cancel();
        _validationCts?.Dispose();
        _validationCts = new CancellationTokenSource();
        var token = _validationCts.Token;

        _ = ValidateFfmpegPathDebouncedAsync(value, token);
    }

    private void ValidateFfmpegPathSync(string path)
    {
        var candidate = !string.IsNullOrWhiteSpace(path) ? path : null;
        var paths = _locator.Locate(candidate);
        ApplyValidationResult(paths, path);
    }

    private async Task ValidateFfmpegPathDebouncedAsync(string path, CancellationToken token)
    {
        try
        {
            await Task.Delay(200, token).ConfigureAwait(true);
            if (token.IsCancellationRequested) return;

            var candidate = !string.IsNullOrWhiteSpace(path) ? path : null;
            var paths = await Task.Run(() => _locator.Locate(candidate), token).ConfigureAwait(true);

            if (token.IsCancellationRequested) return;

            ApplyValidationResult(paths, path);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ApplyValidationResult(FfmpegPaths paths, string originalPath)
    {
        if (paths.IsFfmpegAvailable)
        {
            IsFfmpegValid = true;
            FfmpegVersionText = paths.FfmpegVersion ?? "FFmpeg найден";
        }
        else
        {
            IsFfmpegValid = false;
            FfmpegVersionText = string.IsNullOrWhiteSpace(originalPath)
                ? "Путь не указан (будет использован системный PATH)"
                : "FFmpeg не найден по указанному пути";
        }
    }

    [RelayCommand]
    private async Task BrowseFfmpegAsync()
    {
        var selected = await _fileDialogService.OpenFfmpegExecutableAsync();
        if (!string.IsNullOrEmpty(selected))
        {
            FfmpegPath = selected;
            ValidateFfmpegPathSync(selected);
        }
    }

    [RelayCommand]
    private async Task BrowseOutputDirAsync()
    {
        var selected = await _fileDialogService.OpenFolderAsync(DefaultOutputDir);
        if (!string.IsNullOrEmpty(selected))
        {
            DefaultOutputDir = selected;
        }
    }

    [RelayCommand]
    private void Save()
    {
        var updated = _settingsService.CurrentSettings with
        {
            FfmpegPath = string.IsNullOrWhiteSpace(FfmpegPath) ? null : FfmpegPath.Trim(),
            DefaultOutputDir = string.IsNullOrWhiteSpace(DefaultOutputDir) ? null : DefaultOutputDir.Trim(),
            PreferHardware = PreferHardware
        };

        _settingsService.Save(updated);
        SettingsSaved?.Invoke();
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose?.Invoke();
    }

    public void Dispose()
    {
        _validationCts?.Cancel();
        _validationCts?.Dispose();
        _validationCts = null;
    }
}
