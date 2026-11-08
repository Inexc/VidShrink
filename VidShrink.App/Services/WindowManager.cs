using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using VidShrink.App.ViewModels;
using VidShrink.App.Views;

namespace VidShrink.App.Services;

public class WindowManager : IWindowManager
{
    private readonly Func<SettingsViewModel> _settingsVmFactory;
    private SettingsWindow? _currentSettingsWindow;

    public WindowManager(Func<SettingsViewModel>? settingsVmFactory = null)
    {
        _settingsVmFactory = settingsVmFactory ?? (() => new SettingsViewModel());
    }

    public async Task ShowSettingsDialogAsync()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
        {
            if (_currentSettingsWindow != null)
            {
                if (_currentSettingsWindow.WindowState == WindowState.Minimized)
                {
                    _currentSettingsWindow.WindowState = WindowState.Normal;
                }
                _currentSettingsWindow.Activate();
                return;
            }

            var vm = _settingsVmFactory();
            var window = new SettingsWindow
            {
                DataContext = vm,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };

            var tcs = new TaskCompletionSource<bool>();

            void OnMainClosed(object? s, EventArgs e)
            {
                window.Close();
            }

            desktop.MainWindow.Closed += OnMainClosed;

            vm.RequestClose += () => window.Close();
            window.Closed += (s, e) =>
            {
                if (desktop.MainWindow != null)
                {
                    desktop.MainWindow.Closed -= OnMainClosed;
                }
                _currentSettingsWindow = null;
                tcs.TrySetResult(true);
            };

            _currentSettingsWindow = window;
            window.Show();

            await tcs.Task;
        }
    }
}
