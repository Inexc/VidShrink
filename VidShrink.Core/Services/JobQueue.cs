using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VidShrink.Core.Models;

namespace VidShrink.Core.Services;

public interface IJobQueue : IDisposable
{
    IReadOnlyList<CompressionJob> Jobs { get; }
    CompressionJob? CurrentJob { get; }
    bool IsRunning { get; }

    void Enqueue(CompressionJob job);
    void EnqueueRange(IEnumerable<CompressionJob> jobs);
    bool Remove(string jobId);
    void Clear();

    Task StartAsync(CancellationToken cancellationToken = default);
    void Stop();
    void CancelJob(string jobId);
    void CancelAll();

    event EventHandler<CompressionJob>? JobAdded;
    event EventHandler<CompressionJob>? JobStarted;
    event EventHandler<CompressionJob>? JobProgress;
    event EventHandler<CompressionJob>? JobCompleted;
    event EventHandler<CompressionJob>? JobFailed;
    event EventHandler<CompressionJob>? JobCancelled;
    event EventHandler? QueueCompleted;
}

public class JobQueue : IJobQueue
{
    private readonly IEncoderService _encoderService;
    private readonly List<CompressionJob> _jobs = new();
    private readonly object _lock = new();

    private CancellationTokenSource? _queueCts;
    private CancellationTokenSource? _currentJobCts;
    private bool _disposed;

    public IReadOnlyList<CompressionJob> Jobs
    {
        get
        {
            lock (_lock)
            {
                return _jobs.ToList();
            }
        }
    }

    public CompressionJob? CurrentJob { get; private set; }
    public bool IsRunning { get; private set; }

    public event EventHandler<CompressionJob>? JobAdded;
    public event EventHandler<CompressionJob>? JobStarted;
    public event EventHandler<CompressionJob>? JobProgress;
    public event EventHandler<CompressionJob>? JobCompleted;
    public event EventHandler<CompressionJob>? JobFailed;
    public event EventHandler<CompressionJob>? JobCancelled;
    public event EventHandler? QueueCompleted;

    public JobQueue(IEncoderService encoderService)
    {
        _encoderService = encoderService ?? throw new ArgumentNullException(nameof(encoderService));
    }

    public void Enqueue(CompressionJob job)
    {
        if (job == null) throw new ArgumentNullException(nameof(job));

        lock (_lock)
        {
            InitializeJobOriginalSize(job);
            _jobs.Add(job);
        }

        JobAdded?.Invoke(this, job);
    }

    public void EnqueueRange(IEnumerable<CompressionJob> jobs)
    {
        if (jobs == null) throw new ArgumentNullException(nameof(jobs));

        foreach (var job in jobs)
        {
            Enqueue(job);
        }
    }

    public bool Remove(string jobId)
    {
        lock (_lock)
        {
            if (CurrentJob?.Id == jobId)
            {
                _currentJobCts?.Cancel();
            }

            var index = _jobs.FindIndex(j => j.Id == jobId);
            if (index >= 0)
            {
                _jobs.RemoveAt(index);
                return true;
            }
            return false;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            CancelAll();
            _jobs.Clear();
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (IsRunning)
                return;

            IsRunning = true;
            _queueCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        try
        {
            while (true)
            {
                CompressionJob? jobToProcess;

                lock (_lock)
                {
                    if (_queueCts == null || _queueCts.IsCancellationRequested)
                        break;

                    jobToProcess = _jobs.FirstOrDefault(j => j.Status == JobStatus.Queued);
                    if (jobToProcess == null)
                        break;

                    CurrentJob = jobToProcess;
                    _currentJobCts = CancellationTokenSource.CreateLinkedTokenSource(_queueCts.Token);
                }

                await ProcessJobAsync(jobToProcess, _currentJobCts.Token).ConfigureAwait(false);

                lock (_lock)
                {
                    _currentJobCts?.Dispose();
                    _currentJobCts = null;
                    CurrentJob = null;
                }
            }
        }
        finally
        {
            lock (_lock)
            {
                IsRunning = false;
                CurrentJob = null;
                _queueCts?.Dispose();
                _queueCts = null;
            }

            QueueCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _queueCts?.Cancel();
            _currentJobCts?.Cancel();
        }
    }

    public void CancelJob(string jobId)
    {
        CompressionJob? cancelledJob = null;
        lock (_lock)
        {
            if (CurrentJob?.Id == jobId)
            {
                _currentJobCts?.Cancel();
                return;
            }

            var job = _jobs.FirstOrDefault(j => j.Id == jobId);
            if (job != null && job.Status == JobStatus.Queued)
            {
                job.Status = JobStatus.Cancelled;
                job.StatusMessage = "Отменено";
                job.CompletedAt = DateTime.UtcNow;
                cancelledJob = job;
            }
        }

        if (cancelledJob != null)
        {
            JobCancelled?.Invoke(this, cancelledJob);
        }
    }

    public void CancelAll()
    {
        List<CompressionJob> cancelledJobs;
        lock (_lock)
        {
            cancelledJobs = _jobs.Where(j => j.Status == JobStatus.Queued).ToList();
            foreach (var job in cancelledJobs)
            {
                job.Status = JobStatus.Cancelled;
                job.StatusMessage = "Отменено";
                job.CompletedAt = DateTime.UtcNow;
            }

            _queueCts?.Cancel();
            _currentJobCts?.Cancel();
        }

        foreach (var job in cancelledJobs)
        {
            JobCancelled?.Invoke(this, job);
        }
    }

    private async Task ProcessJobAsync(CompressionJob job, CancellationToken cancellationToken)
    {
        InitializeJobOriginalSize(job);

        // Check free disk space on target destination
        try
        {
            var targetDir = Path.GetDirectoryName(Path.GetFullPath(job.OutputFilePath));
            if (!string.IsNullOrEmpty(targetDir))
            {
                var root = Path.GetPathRoot(targetDir);
                if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
                {
                    var drive = new DriveInfo(root);
                    long requiredBytes = job.Settings.TargetSizeMb.HasValue
                        ? (long)(job.Settings.TargetSizeMb.Value * 1024 * 1024 * 1.2)
                        : (job.OriginalSizeBytes > 0 ? job.OriginalSizeBytes : 50 * 1024 * 1024);

                    if (drive.AvailableFreeSpace < requiredBytes)
                    {
                        job.CompletedAt = DateTime.UtcNow;
                        job.Status = JobStatus.Failed;
                        job.Error = $"Недостаточно свободного места на диске {drive.Name} (доступно {drive.AvailableFreeSpace / (1024 * 1024)} МБ, требуется {requiredBytes / (1024 * 1024)} МБ).";
                        job.StatusMessage = "Недостаточно места на диске";
                        JobFailed?.Invoke(this, job);
                        return;
                    }
                }
            }
        }
        catch
        {
            // In case DriveInfo is not available on specific environments or tests
        }

        job.StartedAt = DateTime.UtcNow;
        bool isTwoPass = job.Settings.TwoPass &&
                         job.Settings.Mode == CompressionMode.TargetSize &&
                         (job.Settings.VideoCodec == VideoCodec.H264 || job.Settings.VideoCodec == VideoCodec.H265) &&
                         job.Settings.HardwareAcceleration == HardwareAcceleration.None;
        job.Status = isTwoPass ? JobStatus.ProcessingPass1 : JobStatus.Processing;
        job.StatusMessage = "Обработка...";
        JobStarted?.Invoke(this, job);

        var progress = new SynchronousProgress<ProgressInfo>(info =>
        {
            job.CurrentProgress = info;
            if (info.TotalPasses == 2)
            {
                job.Status = info.CurrentPass == 1 ? JobStatus.ProcessingPass1 : JobStatus.ProcessingPass2;
                job.StatusMessage = $"Проход {info.CurrentPass}/2 ({info.Percent:F1}%)";
            }
            else
            {
                job.Status = JobStatus.Processing;
                job.StatusMessage = $"{info.Percent:F1}%";
            }
            JobProgress?.Invoke(this, job);
        });

        try
        {
            var result = await _encoderService.EncodeAsync(job.Settings, progress, cancellationToken).ConfigureAwait(false);
            job.Result = result;
            job.CompletedAt = DateTime.UtcNow;
            job.Status = JobStatus.Completed;
            job.StatusMessage = result.RetriedWithLowerBitrate ? "Готово (с повтором x0.92)" : "Готово";
            JobCompleted?.Invoke(this, job);
        }
        catch (OperationCanceledException)
        {
            job.CompletedAt = DateTime.UtcNow;
            job.Status = JobStatus.Cancelled;
            job.StatusMessage = "Отменено";
            JobCancelled?.Invoke(this, job);
        }
        catch (Exception ex)
        {
            job.CompletedAt = DateTime.UtcNow;
            job.Status = JobStatus.Failed;
            job.Error = ex.Message;
            job.StatusMessage = $"Ошибка: {ex.Message}";
            JobFailed?.Invoke(this, job);
        }
    }

    private static void InitializeJobOriginalSize(CompressionJob job)
    {
        if (job.OriginalSizeBytes <= 0)
        {
            if (job.SourceInfo != null && job.SourceInfo.FileSizeBytes > 0)
            {
                job.OriginalSizeBytes = job.SourceInfo.FileSizeBytes;
            }
            else if (File.Exists(job.InputFilePath))
            {
                try
                {
                    job.OriginalSizeBytes = new FileInfo(job.InputFilePath).Length;
                }
                catch
                {
                    // Ignore file read error
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        CancelAll();
        lock (_lock)
        {
            _queueCts?.Dispose();
            _queueCts = null;
            _currentJobCts?.Dispose();
            _currentJobCts = null;
        }
    }
}

internal sealed class SynchronousProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;

    public SynchronousProgress(Action<T> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public void Report(T value)
    {
        _handler(value);
    }
}

