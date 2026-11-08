using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VidShrink.App.Services;
using VidShrink.Core.Models;
using VidShrink.Core.Platform;
using VidShrink.Core.Services;

namespace VidShrink.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IPlatformService _platformService;
    private readonly ISettingsService _settingsService;
    private readonly ILogService _logService;
    private readonly IFfmpegLocator _locator;
    private readonly IProbeService _probeService;
    private readonly IBitrateCalculator _bitrateCalculator;
    private readonly IPresetCatalog _presetCatalog;
    private readonly ICommandBuilder _commandBuilder;
    private readonly IEncoderDetector _encoderDetector;
    private readonly IEncoderService _encoderService;
    private readonly IJobQueue _jobQueue;
    private readonly IFileDialogService _fileDialogService;
    private readonly IWindowManager _windowManager;

    private readonly object _progressThrottleLock = new();
    private readonly Dictionary<string, long> _lastProgressUpdateTimestamp = new();

    [ObservableProperty]
    private string _appTitle = "VidShrink";

    [ObservableProperty]
    private string _platformName = string.Empty;

    [ObservableProperty]
    private string _ffmpegStatusText = "Проверка наличия FFmpeg...";

    [ObservableProperty]
    private bool _isFfmpegAvailable;

    // File / Queue collections
    public ObservableCollection<JobItemViewModel> Jobs { get; } = new();

    [ObservableProperty]
    private JobItemViewModel? _selectedJob;

    [ObservableProperty]
    private bool _hasJobs;

    // Presets & Encoders
    public ObservableCollection<Preset> Presets { get; } = new();

    [ObservableProperty]
    private Preset? _selectedPreset;

    public ObservableCollection<EncoderDescriptor> AvailableEncoders { get; } = new();

    [ObservableProperty]
    private EncoderDescriptor? _selectedEncoder;

    // Settings
    [ObservableProperty]
    private double _targetSizeMb = 16.0;

    [ObservableProperty]
    private bool _twoPass = true;

    [ObservableProperty]
    private string? _outputDirectory;

    [ObservableProperty]
    private string _estimatedBitrateText = string.Empty;

    [ObservableProperty]
    private string? _bitrateWarningMessage;

    [ObservableProperty]
    private bool _hasBitrateWarning;

    // Queue Execution
    [ObservableProperty]
    private bool _isQueueRunning;

    [ObservableProperty]
    private bool _canStartCompression;

    [ObservableProperty]
    private double _queueProgressPercent;

    [ObservableProperty]
    private string _queueStatusText = "Готов к работе";

    public MainWindowViewModel(
        IPlatformService? platformService = null,
        ISettingsService? settingsService = null,
        ILogService? logService = null,
        IFfmpegLocator? locator = null,
        IProbeService? probeService = null,
        IBitrateCalculator? bitrateCalculator = null,
        IPresetCatalog? presetCatalog = null,
        ICommandBuilder? commandBuilder = null,
        IEncoderDetector? encoderDetector = null,
        IEncoderService? encoderService = null,
        IJobQueue? jobQueue = null,
        IFileDialogService? fileDialogService = null,
        IWindowManager? windowManager = null)
    {
        _platformService = platformService ?? DefaultPlatformService.Instance;
        _settingsService = settingsService ?? new SettingsService(_platformService);
        _logService = logService ?? new LogService(_platformService);
        _locator = locator ?? new FfmpegLocator(_platformService, _settingsService);
        _probeService = probeService ?? new ProbeService(_locator);
        _bitrateCalculator = bitrateCalculator ?? new BitrateCalculator();
        _presetCatalog = presetCatalog ?? new PresetCatalog(_platformService);
        _commandBuilder = commandBuilder ?? new CommandBuilder(_platformService, _bitrateCalculator);
        _encoderDetector = encoderDetector ?? new EncoderDetector(_locator, _platformService);
        _encoderService = encoderService ?? new EncoderService(_locator, _commandBuilder, new DefaultProcessRunner(), _platformService, _logService);
        _jobQueue = jobQueue ?? new JobQueue(_encoderService);
        _fileDialogService = fileDialogService ?? new AvaloniaFileDialogService();
        _windowManager = windowManager ?? new WindowManager(() => new SettingsViewModel(_settingsService, _locator, _fileDialogService, _platformService));

        PlatformName = _platformService.CurrentPlatform switch
        {
            PlatformKind.Windows => "Windows",
            PlatformKind.Linux => "Linux",
            _ => _platformService.CurrentPlatform.ToString()
        };

        // Load presets
        foreach (var p in _presetCatalog.GetPresets())
        {
            Presets.Add(p);
        }
        SelectedPreset = Presets.FirstOrDefault();

        // Attach queue event handlers
        _jobQueue.JobStarted += (s, job) => RunOnUi(() => OnJobStarted(job));
        _jobQueue.JobProgress += (s, job) => OnJobProgressThrottled(job);
        _jobQueue.JobCompleted += (s, job) => RunOnUi(() => OnJobCompleted(job));
        _jobQueue.JobFailed += (s, job) => RunOnUi(() => OnJobFailed(job));
        _jobQueue.JobCancelled += (s, job) => RunOnUi(() => OnJobCancelled(job));
        _jobQueue.QueueCompleted += (s, e) => RunOnUi(OnQueueCompleted);

        // Initialize settings and FFmpeg asynchronously
        _ = ReloadSettingsAsync();
    }

    public async Task ReloadSettingsAsync()
    {
        var settings = _settingsService.Load();
        if (!string.IsNullOrWhiteSpace(settings.DefaultOutputDir))
        {
            OutputDirectory = settings.DefaultOutputDir;
        }

        var paths = await Task.Run(() => _locator.Locate(settings.FfmpegPath)).ConfigureAwait(true);
        IsFfmpegAvailable = paths.IsFullyAvailable;

        if (IsFfmpegAvailable)
        {
            string verStr = "найден";
            if (!string.IsNullOrWhiteSpace(paths.FfmpegVersion))
            {
                var parts = paths.FfmpegVersion.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 3) verStr = parts[2];
                else if (parts.Length >= 1) verStr = parts[^1];
            }
            FfmpegStatusText = $"FFmpeg готов ({verStr})";
            var detected = await _encoderDetector.DetectAvailableEncodersAsync(paths.FfmpegPath, verifyWithTestEncode: false);
            AvailableEncoders.Clear();
            foreach (var enc in detected)
            {
                if (!settings.PreferHardware && enc.Acceleration != HardwareAcceleration.None)
                {
                    continue;
                }
                AvailableEncoders.Add(enc);
            }
            if (SelectedEncoder == null || !AvailableEncoders.Contains(SelectedEncoder))
            {
                SelectedEncoder = AvailableEncoders.FirstOrDefault(e => e.Codec == VideoCodec.H264) ?? AvailableEncoders.FirstOrDefault();
            }
        }
        else
        {
            FfmpegStatusText = _platformService.IsLinux
                ? "FFmpeg не найден. Установите: sudo apt install ffmpeg"
                : "FFmpeg/FFprobe не найден. Укажите путь в настройках.";
        }

        UpdateCanStart();
    }

    [RelayCommand]
    public async Task DropFilesAsync(IEnumerable<string> filePaths)
    {
        if (filePaths == null) return;
        await AddFilesAsync(filePaths);
    }

    [RelayCommand]
    public async Task BrowseInputFilesAsync()
    {
        var files = await _fileDialogService.OpenVideoFilesAsync();
        if (files.Count > 0)
        {
            await AddFilesAsync(files);
        }
    }

    [RelayCommand]
    public async Task BrowseOutputDirectoryAsync()
    {
        var folder = await _fileDialogService.OpenFolderAsync(OutputDirectory);
        if (!string.IsNullOrEmpty(folder))
        {
            OutputDirectory = folder;
            // Update queued output paths if output folder changed
            UpdateQueuedOutputPaths(folder);
        }
    }

    [RelayCommand]
    public async Task OpenSettingsAsync()
    {
        await _windowManager.ShowSettingsDialogAsync();
        await ReloadSettingsAsync();
    }

    public async Task AddFilesAsync(IEnumerable<string> paths)
    {
        var pathList = paths?.Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p)).Distinct().ToList();
        if (pathList == null || pathList.Count == 0) return;

        var existingPaths = new HashSet<string>(Jobs.Select(j => j.InputFilePath), StringComparer.OrdinalIgnoreCase);
        var newPaths = pathList.Where(p => !existingPaths.Contains(p)).ToList();
        if (newPaths.Count == 0) return;

        int degreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 2, 4);
        using var semaphore = new SemaphoreSlim(degreeOfParallelism);

        var probeTasks = newPaths.Select(async path =>
        {
            await semaphore.WaitAsync().ConfigureAwait(false);
            MediaInfo? info = null;
            string? fileError = null;
            try
            {
                info = await _probeService.ProbeAsync(path).ConfigureAwait(false);
                if (info == null || !info.HasVideo)
                {
                    fileError = "Файл повреждён или не содержит видеопотока.";
                }
            }
            catch (Exception ex)
            {
                fileError = $"Файл повреждён или не поддерживается ({ex.Message})";
            }
            finally
            {
                semaphore.Release();
            }

            return (Path: path, Info: info, Error: fileError);
        });

        var probedResults = await Task.WhenAll(probeTasks).ConfigureAwait(true);

        foreach (var (path, info, fileError) in probedResults)
        {
            var outDir = !string.IsNullOrWhiteSpace(OutputDirectory) ? OutputDirectory : Path.GetDirectoryName(path);
            var safeOutput = PathHelper.GenerateUniqueOutputPath(path, outDir);

            var settings = CreateSettingsForFile(path, safeOutput, info?.Duration ?? TimeSpan.FromSeconds(60));

            var job = new CompressionJob
            {
                InputFilePath = path,
                OutputFilePath = safeOutput,
                Settings = settings,
                SourceInfo = info,
                OriginalSizeBytes = info?.FileSizeBytes ?? (File.Exists(path) ? new FileInfo(path).Length : 0),
                Status = fileError != null ? JobStatus.Failed : JobStatus.Queued,
                StatusMessage = fileError != null ? "Повреждённый файл" : "В очереди",
                Error = fileError
            };

            var jobVm = new JobItemViewModel(
                job,
                _platformService,
                onCancel: id => _jobQueue.CancelJob(id),
                onRemove: id => RemoveJob(id));

            if (fileError != null)
            {
                jobVm.UpdateStatus(JobStatus.Failed, "Повреждённый файл", fileError);
            }

            Jobs.Add(jobVm);
            if (fileError == null)
            {
                _jobQueue.Enqueue(job);
            }
        }

        HasJobs = Jobs.Count > 0;
        if (SelectedJob == null && Jobs.Count > 0)
        {
            SelectedJob = Jobs[0];
        }

        UpdateEstimatedBitrate();
        UpdateCanStart();
    }

    [RelayCommand]
    public void RemoveJob(string id)
    {
        _jobQueue.Remove(id);
        var existing = Jobs.FirstOrDefault(j => j.Id == id);
        if (existing != null)
        {
            Jobs.Remove(existing);
        }
        lock (_progressThrottleLock)
        {
            _lastProgressUpdateTimestamp.Remove(id);
        }
        HasJobs = Jobs.Count > 0;
        if (SelectedJob?.Id == id)
        {
            SelectedJob = Jobs.FirstOrDefault();
        }
        UpdateEstimatedBitrate();
        UpdateCanStart();
    }

    [RelayCommand]
    public void ClearJobs()
    {
        _jobQueue.Clear();
        Jobs.Clear();
        lock (_progressThrottleLock)
        {
            _lastProgressUpdateTimestamp.Clear();
        }
        SelectedJob = null;
        HasJobs = false;
        EstimatedBitrateText = string.Empty;
        BitrateWarningMessage = null;
        HasBitrateWarning = false;
        UpdateCanStart();
    }

    [RelayCommand]
    public void SetTargetSize(string sizeStr)
    {
        if (double.TryParse(sizeStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var size))
        {
            TargetSizeMb = size;
        }
    }

    [RelayCommand]
    public async Task StartCompressionAsync()
    {
        if (!CanStartCompression) return;

        IsQueueRunning = true;
        QueueStatusText = "Запуск сжатия...";
        QueueProgressPercent = 0;
        UpdateCanStart();

        try
        {
            await _jobQueue.StartAsync();
        }
        catch (Exception ex)
        {
            QueueStatusText = $"Ошибка очереди: {ex.Message}";
        }
        finally
        {
            IsQueueRunning = false;
            UpdateCanStart();
        }
    }

    [RelayCommand]
    public void CancelCompression()
    {
        _jobQueue.CancelAll();
        IsQueueRunning = false;
        QueueStatusText = "Сжатие отменено";
        UpdateCanStart();
    }

    partial void OnTargetSizeMbChanged(double value)
    {
        UpdateEstimatedBitrate();
        UpdateQueuedJobsSettings();
    }

    partial void OnTwoPassChanged(bool value)
    {
        UpdateQueuedJobsSettings();
    }

    partial void OnSelectedEncoderChanged(EncoderDescriptor? value)
    {
        UpdateQueuedJobsSettings();
    }

    partial void OnSelectedPresetChanged(Preset? value)
    {
        if (value == null) return;

        if (value.TargetSizeMb.HasValue)
        {
            TargetSizeMb = value.TargetSizeMb.Value;
        }

        TwoPass = value.TwoPass;

        if (AvailableEncoders.Count > 0)
        {
            var match = AvailableEncoders.FirstOrDefault(e => e.Codec == value.VideoCodec);
            if (match != null)
            {
                SelectedEncoder = match;
            }
        }

        UpdateEstimatedBitrate();
        UpdateQueuedJobsSettings();
    }

    partial void OnSelectedJobChanged(JobItemViewModel? value)
    {
        UpdateEstimatedBitrate();
    }

    private void UpdateEstimatedBitrate()
    {
        var job = SelectedJob ?? Jobs.FirstOrDefault();
        var duration = job?.Job.SourceInfo?.Duration ?? TimeSpan.Zero;

        if (duration <= TimeSpan.Zero || TargetSizeMb <= 0)
        {
            EstimatedBitrateText = string.Empty;
            BitrateWarningMessage = null;
            HasBitrateWarning = false;
            return;
        }

        try
        {
            var calc = _bitrateCalculator.Calculate(TargetSizeMb, duration, audioBitrateKbps: 128);
            EstimatedBitrateText = $"Расчетный видеобитрейт: {calc.VideoBitrateKbps:N0} kbps (аудио: {calc.AudioBitrateKbps} kbps, ~{calc.EstimatedFileSizeBytes / (1024.0 * 1024.0):F1} МБ)";

            if (calc.IsClamped || !string.IsNullOrEmpty(calc.Message))
            {
                BitrateWarningMessage = calc.Message ?? "слишком маленький лимит для этого видео, уменьши разрешение";
                HasBitrateWarning = true;
            }
            else
            {
                BitrateWarningMessage = null;
                HasBitrateWarning = false;
            }
        }
        catch
        {
            EstimatedBitrateText = string.Empty;
            BitrateWarningMessage = null;
            HasBitrateWarning = false;
        }
    }

    private void UpdateQueuedJobsSettings()
    {
        foreach (var jobVm in Jobs.Where(j => j.Status == JobStatus.Queued))
        {
            var dur = jobVm.Job.SourceInfo?.Duration ?? TimeSpan.FromSeconds(60);
            jobVm.Job.Settings = CreateSettingsForFile(jobVm.Job.InputFilePath, jobVm.Job.OutputFilePath, dur);
        }
    }

    private void UpdateQueuedOutputPaths(string folder)
    {
        foreach (var jobVm in Jobs.Where(j => j.Status == JobStatus.Queued))
        {
            var safe = PathHelper.GenerateUniqueOutputPath(jobVm.Job.InputFilePath, folder);
            jobVm.Job.OutputFilePath = safe;
            jobVm.OutputFilePath = safe;
        }
    }

    private CompressionSettings CreateSettingsForFile(string inputPath, string outputPath, TimeSpan duration)
    {
        var preset = SelectedPreset;
        var encoder = SelectedEncoder;

        return new CompressionSettings
        {
            InputFilePath = inputPath,
            OutputFilePath = outputPath,
            Mode = preset?.Mode ?? CompressionMode.TargetSize,
            TargetSizeMb = (preset?.Mode ?? CompressionMode.TargetSize) == CompressionMode.TargetSize ? TargetSizeMb : null,
            Crf = preset?.Crf,
            VideoCodec = encoder?.Codec ?? preset?.VideoCodec ?? VideoCodec.H264,
            HardwareAcceleration = encoder?.Acceleration ?? HardwareAcceleration.None,
            EncoderName = encoder?.Name,
            TwoPass = TwoPass && (encoder == null || encoder.Acceleration == HardwareAcceleration.None),
            AudioMode = preset?.AudioMode ?? AudioMode.Aac,
            AudioBitrateKbps = preset?.AudioBitrateKbps ?? 128,
            Speed = preset?.Speed ?? SpeedPreset.Medium,
            Duration = duration,
            OutputWidth = preset?.MaxWidth,
            OutputHeight = preset?.MaxHeight,
            FpsLimit = preset?.FpsLimit
        };
    }

    private void UpdateCanStart()
    {
        CanStartCompression = IsFfmpegAvailable && !IsQueueRunning && Jobs.Any(j => j.Status == JobStatus.Queued);
    }

    private void OnJobStarted(CompressionJob job)
    {
        var vm = Jobs.FirstOrDefault(j => j.Id == job.Id);
        vm?.UpdateStatus(job.Status, job.StatusMessage);

        int total = Jobs.Count;
        int completed = Jobs.Count(j => j.Status == JobStatus.Completed);
        var fileName = Path.GetFileName(job.InputFilePath);
        QueueStatusText = $"Сжатие: {fileName} ({completed + 1} из {total})...";
        UpdateCanStart();
    }

    private void OnJobProgressThrottled(CompressionJob job)
    {
        var currentProgress = job.CurrentProgress;
        if (currentProgress == null) return;

        bool isTerminal = currentProgress.Percent >= 99.9 || currentProgress.Eta == TimeSpan.Zero;
        long now = Stopwatch.GetTimestamp();

        lock (_progressThrottleLock)
        {
            if (_lastProgressUpdateTimestamp.TryGetValue(job.Id, out var lastTick))
            {
                double elapsedMs = (now - lastTick) * 1000.0 / Stopwatch.Frequency;
                if (elapsedMs < 60 && !isTerminal)
                {
                    return;
                }
            }

            _lastProgressUpdateTimestamp[job.Id] = now;
        }

        RunOnUi(() => OnJobProgress(job));
    }

    private void OnJobProgress(CompressionJob job)
    {
        var vm = Jobs.FirstOrDefault(j => j.Id == job.Id);
        if (job.CurrentProgress != null)
        {
            vm?.UpdateProgress(job.CurrentProgress);
        }

        QueueProgressPercent = ComputeOverallProgress();
    }

    private void OnJobCompleted(CompressionJob job)
    {
        var vm = Jobs.FirstOrDefault(j => j.Id == job.Id);
        if (job.Result != null)
        {
            vm?.UpdateResult(job.Result);
        }

        lock (_progressThrottleLock)
        {
            _lastProgressUpdateTimestamp.Remove(job.Id);
        }

        QueueProgressPercent = ComputeOverallProgress();
        UpdateCanStart();
    }

    private void OnJobFailed(CompressionJob job)
    {
        var vm = Jobs.FirstOrDefault(j => j.Id == job.Id);
        vm?.UpdateStatus(JobStatus.Failed, "Ошибка сжатия", job.Error);

        lock (_progressThrottleLock)
        {
            _lastProgressUpdateTimestamp.Remove(job.Id);
        }

        UpdateCanStart();
    }

    private void OnJobCancelled(CompressionJob job)
    {
        var vm = Jobs.FirstOrDefault(j => j.Id == job.Id);
        vm?.UpdateStatus(JobStatus.Cancelled, "Отменено");

        lock (_progressThrottleLock)
        {
            _lastProgressUpdateTimestamp.Remove(job.Id);
        }

        UpdateCanStart();
    }

    private void OnQueueCompleted()
    {
        IsQueueRunning = false;
        QueueProgressPercent = 100;
        int completed = Jobs.Count(j => j.Status == JobStatus.Completed);
        int failed = Jobs.Count(j => j.Status == JobStatus.Failed);
        QueueStatusText = failed == 0
            ? $"Все задачи выполнены ({completed} готово)"
            : $"Очередь завершена ({completed} готово, {failed} с ошибкой)";
        UpdateCanStart();
    }

    private double ComputeOverallProgress()
    {
        int count = Jobs.Count;
        if (count == 0) return 0.0;
        double total = 0.0;
        for (int i = 0; i < count; i++)
        {
            var job = Jobs[i];
            total += job.Status == JobStatus.Completed ? 100.0 : job.ProgressPercent;
        }
        return Math.Round(total / count, 1);
    }

    private static void RunOnUi(Action action)
    {
        try
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                action();
            }
            else
            {
                Dispatcher.UIThread.Post(action);
            }
        }
        catch
        {
            action();
        }
    }
}
