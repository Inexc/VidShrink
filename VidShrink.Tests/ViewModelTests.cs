using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VidShrink.App.Services;
using VidShrink.App.ViewModels;
using VidShrink.Core.Models;
using VidShrink.Core.Platform;
using VidShrink.Core.Services;
using Xunit;

namespace VidShrink.Tests;

public class MockFileDialogService : IFileDialogService
{
    public List<string> VideoFilesToReturn { get; set; } = new();
    public string? FfmpegExecutableToReturn { get; set; }
    public string? FolderToReturn { get; set; }

    public Task<IReadOnlyList<string>> OpenVideoFilesAsync() => Task.FromResult<IReadOnlyList<string>>(VideoFilesToReturn);
    public Task<string?> OpenFfmpegExecutableAsync() => Task.FromResult(FfmpegExecutableToReturn);
    public Task<string?> OpenFolderAsync(string? initialDirectory = null) => Task.FromResult(FolderToReturn);
}

public class MockWindowManager : IWindowManager
{
    public bool ShowSettingsCalled { get; private set; }
    public Action? OnShowSettings { get; set; }

    public Task ShowSettingsDialogAsync()
    {
        ShowSettingsCalled = true;
        OnShowSettings?.Invoke();
        return Task.CompletedTask;
    }
}

public class MockSettingsService : ISettingsService
{
    public AppSettings CurrentSettings { get; private set; } = new();
    public string SettingsFilePath => "mock_settings.json";

    public AppSettings Load() => CurrentSettings;

    public void Save(AppSettings settings)
    {
        CurrentSettings = settings;
    }
}

public class MockProbeService : IProbeService
{
    public MediaInfo ResultToReturn { get; set; } = new()
    {
        Duration = TimeSpan.FromMinutes(2),
        FileSizeBytes = 50 * 1024 * 1024,
        FormatName = "mov,mp4,m4a,3gp,3g2,mj2",
        VideoStreams = new List<VideoStreamInfo>
        {
            new()
            {
                CodecName = "h264",
                Width = 1920,
                Height = 1080,
                FrameRate = 30
            }
        },
        AudioStreams = new List<AudioStreamInfo>
        {
            new()
            {
                CodecName = "aac",
                Bitrate = 128_000
            }
        }
    };

    public MediaInfo ParseJson(string json, string? filePath = null) => ResultToReturn;

    public Task<MediaInfo> ProbeAsync(string filePath, string? ffprobePath = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ResultToReturn);
    }
}

public class SettingsViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TestPlatformService _platformService;
    private readonly MockSettingsService _settingsService;
    private readonly MockFileDialogService _fileDialogService;

    public SettingsViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"VidShrinkSettingsVmTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _platformService = new TestPlatformService { CurrentPlatform = PlatformKind.Windows, ConfigDirectory = _tempDir };
        _settingsService = new MockSettingsService();
        _fileDialogService = new MockFileDialogService();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public void Constructor_LoadsInitialSettings()
    {
        _settingsService.Save(new AppSettings
        {
            FfmpegPath = @"C:\tools\ffmpeg.exe",
            DefaultOutputDir = @"D:\Videos",
            PreferHardware = false
        });

        var vm = new SettingsViewModel(_settingsService, fileDialogService: _fileDialogService, platformService: _platformService);

        Assert.Equal(@"C:\tools\ffmpeg.exe", vm.FfmpegPath);
        Assert.Equal(@"D:\Videos", vm.DefaultOutputDir);
        Assert.False(vm.PreferHardware);
    }

    [Fact]
    public async Task BrowseCommands_UpdatePaths()
    {
        var vm = new SettingsViewModel(_settingsService, fileDialogService: _fileDialogService, platformService: _platformService);

        _fileDialogService.FfmpegExecutableToReturn = @"C:\ffmpeg\bin\ffmpeg.exe";
        _fileDialogService.FolderToReturn = @"D:\Output";

        await vm.BrowseFfmpegCommand.ExecuteAsync(null);
        await vm.BrowseOutputDirCommand.ExecuteAsync(null);

        Assert.Equal(@"C:\ffmpeg\bin\ffmpeg.exe", vm.FfmpegPath);
        Assert.Equal(@"D:\Output", vm.DefaultOutputDir);
    }

    [Fact]
    public void SaveCommand_PersistsSettingsAndInvokesEvents()
    {
        var vm = new SettingsViewModel(_settingsService, fileDialogService: _fileDialogService, platformService: _platformService)
        {
            FfmpegPath = @"C:\custom\ffmpeg.exe",
            DefaultOutputDir = @"D:\CustomOutput",
            PreferHardware = true
        };

        bool savedFired = false;
        bool closeFired = false;

        vm.SettingsSaved += () => savedFired = true;
        vm.RequestClose += () => closeFired = true;

        vm.SaveCommand.Execute(null);

        Assert.True(savedFired);
        Assert.True(closeFired);

        var saved = _settingsService.CurrentSettings;
        Assert.Equal(@"C:\custom\ffmpeg.exe", saved.FfmpegPath);
        Assert.Equal(@"D:\CustomOutput", saved.DefaultOutputDir);
        Assert.True(saved.PreferHardware);
    }

    [Fact]
    public void CancelCommand_ClosesWithoutSaving()
    {
        _settingsService.Save(new AppSettings
        {
            FfmpegPath = @"C:\original\ffmpeg.exe"
        });

        var vm = new SettingsViewModel(_settingsService, fileDialogService: _fileDialogService, platformService: _platformService)
        {
            FfmpegPath = @"C:\modified\ffmpeg.exe"
        };

        bool closeFired = false;
        vm.RequestClose += () => closeFired = true;

        vm.CancelCommand.Execute(null);

        Assert.True(closeFired);
        Assert.Equal(@"C:\original\ffmpeg.exe", _settingsService.CurrentSettings.FfmpegPath);
    }
}

public class MainWindowViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TestPlatformService _platformService;
    private readonly MockSettingsService _settingsService;
    private readonly MockFileDialogService _fileDialogService;
    private readonly MockWindowManager _windowManager;
    private readonly MockProbeService _probeService;

    public MainWindowViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"VidShrinkMainWindowVmTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _platformService = new TestPlatformService { CurrentPlatform = PlatformKind.Windows, ConfigDirectory = _tempDir };
        _settingsService = new MockSettingsService();
        _fileDialogService = new MockFileDialogService();
        _windowManager = new MockWindowManager();
        _probeService = new MockProbeService();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch { }
    }

    private MainWindowViewModel CreateViewModel()
    {
        return new MainWindowViewModel(
            platformService: _platformService,
            settingsService: _settingsService,
            probeService: _probeService,
            fileDialogService: _fileDialogService,
            windowManager: _windowManager);
    }

    [Fact]
    public void Presets_AreLoadedOnInit()
    {
        var vm = CreateViewModel();

        Assert.NotEmpty(vm.Presets);
        Assert.NotNull(vm.SelectedPreset);
        Assert.Equal("Windows", vm.PlatformName);
    }

    [Fact]
    public void PresetSelection_UpdatesTargetSizeAndTwoPass()
    {
        var vm = CreateViewModel();

        var discord = vm.Presets.FirstOrDefault(p => p.Name.Contains("Discord"));
        Assert.NotNull(discord);

        vm.SelectedPreset = discord;

        Assert.Equal(8.0, vm.TargetSizeMb);
        Assert.True(vm.TwoPass);
    }

    [Fact]
    public void SetTargetSizeCommand_UpdatesTargetSize()
    {
        var vm = CreateViewModel();

        vm.SetTargetSizeCommand.Execute("25.0");
        Assert.Equal(25.0, vm.TargetSizeMb);

        vm.SetTargetSizeCommand.Execute("100");
        Assert.Equal(100.0, vm.TargetSizeMb);
    }

    [Fact]
    public async Task AddFilesAsync_AddsJobsAndCalculatesEstimatedBitrate()
    {
        var vm = CreateViewModel();

        var file1 = Path.Combine(_tempDir, "video1.mp4");
        File.WriteAllText(file1, "dummy video content");

        await vm.AddFilesAsync(new[] { file1 });

        Assert.Single(vm.Jobs);
        Assert.True(vm.HasJobs);
        Assert.NotNull(vm.SelectedJob);
        Assert.Equal("video1.mp4", vm.SelectedJob.FileName);
        Assert.NotEmpty(vm.EstimatedBitrateText);
        Assert.False(vm.HasBitrateWarning);
    }

    [Fact]
    public async Task BitrateWarning_ShowsWhenTargetSizeIsTooSmallForDuration()
    {
        var vm = CreateViewModel();

        // 60 minute video with 1 MB target size -> budget pressure
        _probeService.ResultToReturn = new MediaInfo
        {
            Duration = TimeSpan.FromHours(1),
            FileSizeBytes = 500 * 1024 * 1024
        };

        var file1 = Path.Combine(_tempDir, "long_movie.mp4");
        File.WriteAllText(file1, "dummy");

        await vm.AddFilesAsync(new[] { file1 });

        vm.TargetSizeMb = 1.0;

        Assert.True(vm.HasBitrateWarning);
        Assert.NotNull(vm.BitrateWarningMessage);
        Assert.Contains("уменьши разрешение", vm.BitrateWarningMessage);
    }

    [Fact]
    public async Task ClearJobs_ResetsQueueAndEstimates()
    {
        var vm = CreateViewModel();

        var file1 = Path.Combine(_tempDir, "video.mp4");
        File.WriteAllText(file1, "dummy");

        await vm.AddFilesAsync(new[] { file1 });
        Assert.True(vm.HasJobs);

        vm.ClearJobsCommand.Execute(null);

        Assert.Empty(vm.Jobs);
        Assert.False(vm.HasJobs);
        Assert.Null(vm.SelectedJob);
        Assert.Empty(vm.EstimatedBitrateText);
        Assert.False(vm.HasBitrateWarning);
    }

    [Fact]
    public async Task RemoveJob_RemovesSpecificJob()
    {
        var vm = CreateViewModel();

        var f1 = Path.Combine(_tempDir, "v1.mp4");
        var f2 = Path.Combine(_tempDir, "v2.mp4");
        File.WriteAllText(f1, "1");
        File.WriteAllText(f2, "2");

        await vm.AddFilesAsync(new[] { f1, f2 });
        Assert.Equal(2, vm.Jobs.Count);

        var firstId = vm.Jobs[0].Id;
        vm.RemoveJobCommand.Execute(firstId);

        Assert.Single(vm.Jobs);
        Assert.Equal("v2.mp4", vm.Jobs[0].FileName);
    }

    [Fact]
    public async Task OpenSettingsCommand_OpensDialogAndReloads()
    {
        var vm = CreateViewModel();

        _windowManager.OnShowSettings = () =>
        {
            _settingsService.Save(new AppSettings
            {
                DefaultOutputDir = @"D:\NewOut"
            });
        };

        await vm.OpenSettingsCommand.ExecuteAsync(null);

        Assert.True(_windowManager.ShowSettingsCalled);
        Assert.Equal(@"D:\NewOut", vm.OutputDirectory);
    }

    [Fact]
    public async Task BrowseInputFiles_UsesFileDialogService()
    {
        var vm = CreateViewModel();

        var testFile = Path.Combine(_tempDir, "selected.mp4");
        File.WriteAllText(testFile, "data");

        _fileDialogService.VideoFilesToReturn = new List<string> { testFile };

        await vm.BrowseInputFilesCommand.ExecuteAsync(null);

        Assert.Single(vm.Jobs);
        Assert.Equal("selected.mp4", vm.Jobs[0].FileName);
    }

    [Fact]
    public async Task DropFilesCommand_HandlesList()
    {
        var vm = CreateViewModel();

        var testFile = Path.Combine(_tempDir, "dropped.mp4");
        File.WriteAllText(testFile, "data");

        await vm.DropFilesCommand.ExecuteAsync(new[] { testFile });

        Assert.Single(vm.Jobs);
        Assert.Equal("dropped.mp4", vm.Jobs[0].FileName);
    }
}

public class JobItemViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TestPlatformService _platformService;

    public JobItemViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"VidShrinkJobVmTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _platformService = new TestPlatformService { CurrentPlatform = PlatformKind.Windows, ConfigDirectory = _tempDir };
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch { }
    }

    private static CompressionSettings CreateDummySettings() => new()
    {
        InputFilePath = @"C:\sample.mp4",
        OutputFilePath = @"C:\out.mp4"
    };

    [Fact]
    public void Properties_InitializedCorrectlyFromJob()
    {
        var job = new CompressionJob
        {
            InputFilePath = @"C:\videos\sample.mp4",
            OutputFilePath = @"C:\videos\sample_compressed.mp4",
            Settings = new CompressionSettings
            {
                InputFilePath = @"C:\videos\sample.mp4",
                OutputFilePath = @"C:\videos\sample_compressed.mp4"
            },
            OriginalSizeBytes = 100 * 1024 * 1024,
            SourceInfo = new MediaInfo
            {
                Duration = TimeSpan.FromMinutes(5),
                FileSizeBytes = 100 * 1024 * 1024,
                VideoStreams = new[]
                {
                    new VideoStreamInfo { CodecName = "h264", Width = 1920, Height = 1080 }
                }
            }
        };

        var vm = new JobItemViewModel(job, _platformService);

        Assert.Equal("sample.mp4", vm.FileName);
        Assert.Equal("05:00", vm.DurationText);
        Assert.Equal("1920x1080", vm.ResolutionText);
        Assert.Equal("100.0 МБ", vm.OriginalSizeText);
        Assert.Equal("В очереди", vm.StatusText);
        Assert.False(vm.IsCompleted);
    }

    [Fact]
    public void UpdateProgress_UpdatesPercentSpeedAndEta()
    {
        var job = new CompressionJob
        {
            InputFilePath = @"C:\sample.mp4",
            OutputFilePath = @"C:\out.mp4",
            Settings = CreateDummySettings()
        };
        var vm = new JobItemViewModel(job, _platformService);

        vm.UpdateProgress(new ProgressInfo
        {
            Percent = 45.67,
            SpeedRatio = 2.1,
            Eta = TimeSpan.FromSeconds(35),
            CurrentPass = 1,
            TotalPasses = 2
        });

        Assert.Equal(45.7, vm.ProgressPercent);
        Assert.Equal("2.1x", vm.SpeedText);
        Assert.Equal("00:35", vm.EtaText);
        Assert.Contains("Проход 1/2", vm.StatusText);
    }

    [Fact]
    public void UpdateResult_SuccessCalculatesSavings()
    {
        var job = new CompressionJob
        {
            InputFilePath = @"C:\sample.mp4",
            OutputFilePath = @"C:\out.mp4",
            Settings = CreateDummySettings(),
            OriginalSizeBytes = 100 * 1024 * 1024
        };
        var vm = new JobItemViewModel(job, _platformService);

        vm.UpdateResult(new EncodeResult
        {
            Success = true,
            OutputFilePath = @"C:\out.mp4",
            OutputSizeBytes = 20 * 1024 * 1024,
            RetriedWithLowerBitrate = false
        });

        Assert.True(vm.IsCompleted);
        Assert.Equal("Готово", vm.StatusText);
        Assert.Equal(100, vm.ProgressPercent);
        Assert.Equal("20.0 МБ", vm.CompressedSizeText);
        Assert.Contains("-80.0%", vm.SavingsText);
    }

    [Fact]
    public void Callbacks_CancelAndRemoveAreInvoked()
    {
        var job = new CompressionJob
        {
            InputFilePath = @"C:\sample.mp4",
            OutputFilePath = @"C:\out.mp4",
            Settings = CreateDummySettings()
        };

        string? cancelledId = null;
        string? removedId = null;

        var vm = new JobItemViewModel(
            job,
            _platformService,
            onCancel: id => cancelledId = id,
            onRemove: id => removedId = id);

        vm.CancelJobCommand.Execute(null);
        Assert.Equal(job.Id, cancelledId);

        vm.RemoveJobCommand.Execute(null);
        Assert.Equal(job.Id, removedId);
    }
}
