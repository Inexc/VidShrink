using System;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VidShrink.Core.Models;
using VidShrink.Core.Platform;

namespace VidShrink.App.ViewModels;

public partial class JobItemViewModel : ObservableObject
{
    private readonly IPlatformService _platformService;
    private readonly Action<string>? _onCancel;
    private readonly Action<string>? _onRemove;

    public CompressionJob Job { get; }

    public string Id => Job.Id;
    public string InputFilePath => Job.InputFilePath;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _outputFilePath = string.Empty;

    [ObservableProperty]
    private string _durationText = "00:00";

    [ObservableProperty]
    private string _resolutionText = "—";

    [ObservableProperty]
    private string _originalSizeText = "0 МБ";

    [ObservableProperty]
    private string _compressedSizeText = "—";

    [ObservableProperty]
    private string _savingsText = "—";

    [ObservableProperty]
    private string _statusText = "В очереди";

    [ObservableProperty]
    private JobStatus _status = JobStatus.Queued;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _speedText = string.Empty;

    [ObservableProperty]
    private string _etaText = string.Empty;

    [ObservableProperty]
    private string? _errorText;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private bool _isCompleted;

    [ObservableProperty]
    private bool _isFailed;

    [ObservableProperty]
    private bool _isCancelled;

    [ObservableProperty]
    private bool _canCancel = true;

    [ObservableProperty]
    private bool _canOpenFolder;

    public JobItemViewModel(
        CompressionJob job,
        IPlatformService? platformService = null,
        Action<string>? onCancel = null,
        Action<string>? onRemove = null)
    {
        Job = job ?? throw new ArgumentNullException(nameof(job));
        _platformService = platformService ?? DefaultPlatformService.Instance;
        _onCancel = onCancel;
        _onRemove = onRemove;

        FileName = Path.GetFileName(job.InputFilePath);
        OutputFilePath = job.OutputFilePath;
        Status = job.Status;
        StatusText = job.StatusMessage ?? GetDefaultStatusText(job.Status);

        InitMediaDetails();
        UpdateStateFlags();
    }

    private void InitMediaDetails()
    {
        if (Job.SourceInfo != null)
        {
            var dur = Job.SourceInfo.Duration;
            DurationText = dur.TotalHours >= 1
                ? dur.ToString(@"hh\:mm\:ss")
                : dur.ToString(@"mm\:ss");

            var video = Job.SourceInfo.PrimaryVideoStream;
            ResolutionText = video?.ResolutionText ?? "—";

            OriginalSizeText = FormattableString.Invariant($"{Job.SourceInfo.FileSizeMb:F1} МБ");
        }
        else if (Job.OriginalSizeBytes > 0)
        {
            OriginalSizeText = FormattableString.Invariant($"{Job.OriginalSizeBytes / (1024.0 * 1024.0):F1} МБ");
        }
        else
        {
            OriginalSizeText = "—";
        }
    }

    public void UpdateProgress(ProgressInfo info)
    {
        ProgressPercent = Math.Round(info.Percent, 1);
        SpeedText = info.SpeedRatio > 0 ? FormattableString.Invariant($"{info.SpeedRatio:F1}x") : string.Empty;
        EtaText = info.Eta.HasValue
            ? (info.Eta.Value.TotalHours >= 1 ? info.Eta.Value.ToString(@"hh\:mm\:ss") : info.Eta.Value.ToString(@"mm\:ss"))
            : string.Empty;

        if (info.TotalPasses == 2)
        {
            StatusText = FormattableString.Invariant($"Проход {info.CurrentPass}/2 ({info.Percent:F1}%)");
        }
        else
        {
            StatusText = FormattableString.Invariant($"Сжатие... {info.Percent:F1}%");
        }
    }

    public void UpdateStatus(JobStatus newStatus, string? message = null, string? error = null)
    {
        Status = newStatus;
        StatusText = message ?? GetDefaultStatusText(newStatus);
        ErrorText = error;

        UpdateStateFlags();
    }

    public void UpdateResult(EncodeResult result)
    {
        if (result.Success)
        {
            Status = JobStatus.Completed;
            StatusText = result.RetriedWithLowerBitrate ? "Готово (с автоповтором x0.92)" : "Готово";
            ProgressPercent = 100;

            double compMb = result.OutputSizeBytes / (1024.0 * 1024.0);
            CompressedSizeText = FormattableString.Invariant($"{compMb:F1} МБ");

            if (Job.OriginalSizeBytes > 0)
            {
                double savedBytes = Job.OriginalSizeBytes - result.OutputSizeBytes;
                double savedMb = savedBytes / (1024.0 * 1024.0);
                double pct = Math.Round((savedBytes / Job.OriginalSizeBytes) * 100.0, 1);

                SavingsText = pct >= 0
                    ? FormattableString.Invariant($"-{pct:F1}% (-{savedMb:F1} МБ)")
                    : FormattableString.Invariant($"+{Math.Abs(pct):F1}% (+{Math.Abs(savedMb):F1} МБ)");
            }
        }
        else
        {
            Status = JobStatus.Failed;
            StatusText = "Ошибка сжатия";
            ErrorText = result.ErrorMessage;
        }

        UpdateStateFlags();
    }

    private void UpdateStateFlags()
    {
        IsProcessing = Status == JobStatus.Processing || Status == JobStatus.ProcessingPass1 || Status == JobStatus.ProcessingPass2;
        IsCompleted = Status == JobStatus.Completed;
        IsFailed = Status == JobStatus.Failed;
        IsCancelled = Status == JobStatus.Cancelled;

        CanCancel = Status == JobStatus.Queued || IsProcessing;
        CanOpenFolder = IsCompleted && File.Exists(OutputFilePath);
    }

    private static string GetDefaultStatusText(JobStatus status) => status switch
    {
        JobStatus.Queued => "В очереди",
        JobStatus.ProcessingPass1 => "Проход 1/2",
        JobStatus.ProcessingPass2 => "Проход 2/2",
        JobStatus.Processing => "Сжатие...",
        JobStatus.Completed => "Готово",
        JobStatus.Failed => "Ошибка",
        JobStatus.Cancelled => "Отменено",
        _ => status.ToString()
    };

    [RelayCommand]
    private void CancelJob()
    {
        _onCancel?.Invoke(Id);
    }

    [RelayCommand]
    private void RemoveJob()
    {
        _onRemove?.Invoke(Id);
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (!File.Exists(OutputFilePath)) return;

        var cmd = _platformService.GetOpenFolderCommand();
        var args = _platformService.GetOpenFolderArguments(OutputFilePath);

        var psi = new ProcessStartInfo
        {
            FileName = cmd,
            UseShellExecute = false
        };

        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        try
        {
            Process.Start(psi);
        }
        catch
        {
            // Fallback: directory only
            try
            {
                var dir = Path.GetDirectoryName(OutputFilePath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = cmd,
                        UseShellExecute = false,
                        ArgumentList = { dir }
                    });
                }
            }
            catch { }
        }
    }
}
