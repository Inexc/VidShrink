using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VidShrink.Core.Models;
using VidShrink.Core.Services;
using Xunit;

namespace VidShrink.Tests;

public class MockEncoderService : IEncoderService
{
    public event EventHandler<string>? MessageLogged;

    public Func<CompressionSettings, IProgress<ProgressInfo>?, CancellationToken, Task<EncodeResult>>? OnEncodeAsync { get; set; }

    public List<CompressionSettings> HandledSettings { get; } = new();

    public Task<EncodeResult> EncodeAsync(
        CompressionSettings settings,
        IProgress<ProgressInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        HandledSettings.Add(settings);

        if (OnEncodeAsync != null)
        {
            return OnEncodeAsync(settings, progress, cancellationToken);
        }

        return Task.FromResult(new EncodeResult
        {
            Success = true,
            OutputFilePath = settings.OutputFilePath,
            OutputSizeBytes = 5_000_000,
            ElapsedTime = TimeSpan.FromSeconds(1)
        });
    }

    public void RaiseMessage(string message) => MessageLogged?.Invoke(this, message);
}

public class JobQueueTests
{
    private static CompressionSettings CreateDummySettings(string name) => new()
    {
        InputFilePath = $"{name}_in.mp4",
        OutputFilePath = $"{name}_out.mp4",
        Duration = TimeSpan.FromSeconds(60),
        Mode = CompressionMode.TargetSize,
        TargetSizeMb = 10,
        TwoPass = false
    };

    [Fact]
    public async Task StartAsync_ProcessesJobsSequentially()
    {
        var mockEncoder = new MockEncoderService();
        var executionOrder = new List<string>();

        mockEncoder.OnEncodeAsync = async (settings, progress, token) =>
        {
            executionOrder.Add(settings.InputFilePath);
            await Task.Delay(20, token);
            return new EncodeResult
            {
                Success = true,
                OutputFilePath = settings.OutputFilePath,
                OutputSizeBytes = 4_000_000,
                ElapsedTime = TimeSpan.FromMilliseconds(20)
            };
        };

        using var queue = new JobQueue(mockEncoder);

        var job1 = new CompressionJob
        {
            InputFilePath = "job1_in.mp4",
            OutputFilePath = "job1_out.mp4",
            Settings = CreateDummySettings("job1"),
            OriginalSizeBytes = 10_000_000
        };
        var job2 = new CompressionJob
        {
            InputFilePath = "job2_in.mp4",
            OutputFilePath = "job2_out.mp4",
            Settings = CreateDummySettings("job2"),
            OriginalSizeBytes = 20_000_000
        };

        queue.Enqueue(job1);
        queue.Enqueue(job2);

        bool completedFired = false;
        queue.QueueCompleted += (_, _) => completedFired = true;

        await queue.StartAsync();

        Assert.Equal(2, executionOrder.Count);
        Assert.Equal("job1_in.mp4", executionOrder[0]);
        Assert.Equal("job2_in.mp4", executionOrder[1]);

        Assert.Equal(JobStatus.Completed, job1.Status);
        Assert.Equal(JobStatus.Completed, job2.Status);
        Assert.True(completedFired);
    }

    [Fact]
    public async Task SavingsCalculation_ComputesSavedBytesAndPercentageAccurately()
    {
        var mockEncoder = new MockEncoderService
        {
            OnEncodeAsync = (_, _, _) => Task.FromResult(new EncodeResult
            {
                Success = true,
                OutputFilePath = "out.mp4",
                OutputSizeBytes = 2_500_000, // 2.5 MB
                ElapsedTime = TimeSpan.FromSeconds(1)
            })
        };

        using var queue = new JobQueue(mockEncoder);

        var job = new CompressionJob
        {
            InputFilePath = "sample_in.mp4",
            OutputFilePath = "out.mp4",
            Settings = CreateDummySettings("sample"),
            OriginalSizeBytes = 10_000_000 // 10 MB
        };

        queue.Enqueue(job);
        await queue.StartAsync();

        Assert.Equal(JobStatus.Completed, job.Status);
        Assert.Equal(2_500_000, job.CompressedSizeBytes);
        Assert.Equal(7_500_000, job.SavedBytes); // 10M - 2.5M = 7.5M saved
        Assert.Equal(75.0, job.SavingsPercentage); // 75% savings
    }

    [Fact]
    public async Task CancelJob_WhenJobIsRunning_CancelsCurrentJobAndProceedsToNext()
    {
        var mockEncoder = new MockEncoderService();
        var tcsJob1Started = new TaskCompletionSource<bool>();

        mockEncoder.OnEncodeAsync = async (settings, _, token) =>
        {
            if (settings.InputFilePath.Contains("job1"))
            {
                tcsJob1Started.SetResult(true);
                // Wait indefinitely until cancelled
                await Task.Delay(10000, token);
            }

            return new EncodeResult
            {
                Success = true,
                OutputFilePath = settings.OutputFilePath,
                OutputSizeBytes = 1_000_000,
                ElapsedTime = TimeSpan.FromSeconds(1)
            };
        };

        using var queue = new JobQueue(mockEncoder);

        var job1 = new CompressionJob
        {
            InputFilePath = "job1_in.mp4",
            OutputFilePath = "job1_out.mp4",
            Settings = CreateDummySettings("job1")
        };
        var job2 = new CompressionJob
        {
            InputFilePath = "job2_in.mp4",
            OutputFilePath = "job2_out.mp4",
            Settings = CreateDummySettings("job2")
        };

        queue.Enqueue(job1);
        queue.Enqueue(job2);

        var startTask = queue.StartAsync();

        // Wait until job 1 is actively running
        await tcsJob1Started.Task;

        // Cancel job 1
        queue.CancelJob(job1.Id);

        await startTask;

        Assert.Equal(JobStatus.Cancelled, job1.Status);
        Assert.Equal(JobStatus.Completed, job2.Status);
    }

    [Fact]
    public async Task CancelJob_WhenJobIsQueued_MarksAsCancelledAndSkipsExecution()
    {
        var mockEncoder = new MockEncoderService();
        using var queue = new JobQueue(mockEncoder);

        var job1 = new CompressionJob
        {
            InputFilePath = "job1_in.mp4",
            OutputFilePath = "job1_out.mp4",
            Settings = CreateDummySettings("job1")
        };
        var job2 = new CompressionJob
        {
            InputFilePath = "job2_in.mp4",
            OutputFilePath = "job2_out.mp4",
            Settings = CreateDummySettings("job2")
        };

        queue.Enqueue(job1);
        queue.Enqueue(job2);

        // Cancel job2 before starting
        queue.CancelJob(job2.Id);

        await queue.StartAsync();

        Assert.Equal(JobStatus.Completed, job1.Status);
        Assert.Equal(JobStatus.Cancelled, job2.Status);
        Assert.Single(mockEncoder.HandledSettings);
        Assert.Equal("job1_in.mp4", mockEncoder.HandledSettings[0].InputFilePath);
    }

    [Fact]
    public async Task CancelAll_CancelsAllRunningAndQueuedJobs()
    {
        var mockEncoder = new MockEncoderService();
        var tcsStarted = new TaskCompletionSource<bool>();

        mockEncoder.OnEncodeAsync = async (_, _, token) =>
        {
            tcsStarted.TrySetResult(true);
            await Task.Delay(10000, token);
            return new EncodeResult { Success = true, OutputFilePath = "out.mp4" };
        };

        using var queue = new JobQueue(mockEncoder);

        var job1 = new CompressionJob { InputFilePath = "j1.mp4", OutputFilePath = "o1.mp4", Settings = CreateDummySettings("j1") };
        var job2 = new CompressionJob { InputFilePath = "j2.mp4", OutputFilePath = "o2.mp4", Settings = CreateDummySettings("j2") };
        var job3 = new CompressionJob { InputFilePath = "j3.mp4", OutputFilePath = "o3.mp4", Settings = CreateDummySettings("j3") };

        queue.Enqueue(job1);
        queue.Enqueue(job2);
        queue.Enqueue(job3);

        var startTask = queue.StartAsync();
        await tcsStarted.Task;

        queue.CancelAll();
        await startTask;

        Assert.Equal(JobStatus.Cancelled, job1.Status);
        Assert.Equal(JobStatus.Cancelled, job2.Status);
        Assert.Equal(JobStatus.Cancelled, job3.Status);
    }

    [Fact]
    public async Task JobFails_DoesNotStopQueueFromProcessingNextJob()
    {
        var mockEncoder = new MockEncoderService();

        mockEncoder.OnEncodeAsync = (settings, _, _) =>
        {
            if (settings.InputFilePath.Contains("job1"))
            {
                throw new InvalidOperationException("FFmpeg crashed with exit code 1");
            }

            return Task.FromResult(new EncodeResult
            {
                Success = true,
                OutputFilePath = settings.OutputFilePath,
                OutputSizeBytes = 3_000_000
            });
        };

        using var queue = new JobQueue(mockEncoder);

        var job1 = new CompressionJob { InputFilePath = "job1_in.mp4", OutputFilePath = "job1_out.mp4", Settings = CreateDummySettings("job1") };
        var job2 = new CompressionJob { InputFilePath = "job2_in.mp4", OutputFilePath = "job2_out.mp4", Settings = CreateDummySettings("job2") };

        queue.Enqueue(job1);
        queue.Enqueue(job2);

        await queue.StartAsync();

        Assert.Equal(JobStatus.Failed, job1.Status);
        Assert.Contains("FFmpeg crashed", job1.Error);

        Assert.Equal(JobStatus.Completed, job2.Status);
    }

    [Fact]
    public async Task ProgressReporting_UpdatesJobStatusToPass1AndPass2()
    {
        var mockEncoder = new MockEncoderService();

        mockEncoder.OnEncodeAsync = (settings, progress, _) =>
        {
            // Simulate Pass 1 progress
            progress?.Report(new ProgressInfo
            {
                CurrentPass = 1,
                TotalPasses = 2,
                Percent = 25.0,
                PassPercent = 50.0
            });

            // Simulate Pass 2 progress
            progress?.Report(new ProgressInfo
            {
                CurrentPass = 2,
                TotalPasses = 2,
                Percent = 75.0,
                PassPercent = 50.0
            });

            return Task.FromResult(new EncodeResult
            {
                Success = true,
                OutputFilePath = settings.OutputFilePath,
                OutputSizeBytes = 2_000_000
            });
        };

        using var queue = new JobQueue(mockEncoder);

        var statusesRecorded = new List<JobStatus>();
        queue.JobProgress += (_, job) => statusesRecorded.Add(job.Status);

        var job = new CompressionJob
        {
            InputFilePath = "j_in.mp4",
            OutputFilePath = "j_out.mp4",
            Settings = CreateDummySettings("j") with { TwoPass = true }
        };

        queue.Enqueue(job);
        await queue.StartAsync();

        Assert.Contains(JobStatus.ProcessingPass1, statusesRecorded);
        Assert.Contains(JobStatus.ProcessingPass2, statusesRecorded);
        Assert.Equal(JobStatus.Completed, job.Status);
    }

    [Fact]
    public async Task ProcessJobAsync_DelayedInputFile_InitializesOriginalSizeAndCalculatesSavings()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"VidShrinkDelayed_{Guid.NewGuid():N}.mp4");
        try
        {
            var mockEncoder = new MockEncoderService();
            mockEncoder.OnEncodeAsync = (settings, _, _) =>
            {
                return Task.FromResult(new EncodeResult
                {
                    Success = true,
                    OutputFilePath = settings.OutputFilePath,
                    OutputSizeBytes = 4_000_000
                });
            };

            using var queue = new JobQueue(mockEncoder);

            var job = new CompressionJob
            {
                InputFilePath = tempFile,
                OutputFilePath = "out.mp4",
                Settings = CreateDummySettings("delayed"),
                OriginalSizeBytes = 0 // file does not exist yet at enqueue time!
            };

            queue.Enqueue(job);
            Assert.Equal(0, job.OriginalSizeBytes);

            // Now file finishes writing before queue starts:
            File.WriteAllBytes(tempFile, new byte[10_000_000]); // 10 MB

            await queue.StartAsync();

            Assert.Equal(JobStatus.Completed, job.Status);
            Assert.Equal(10_000_000, job.OriginalSizeBytes);
            Assert.Equal(6_000_000, job.SavedBytes);
            Assert.Equal(60.0, job.SavingsPercentage);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void CancelAll_WhenHandlerAccessesQueue_DoesNotDeadlock()
    {
        var mockEncoder = new MockEncoderService();
        using var queue = new JobQueue(mockEncoder);

        var job1 = new CompressionJob { InputFilePath = "1.mp4", OutputFilePath = "1_out.mp4", Settings = CreateDummySettings("1") };
        var job2 = new CompressionJob { InputFilePath = "2.mp4", OutputFilePath = "2_out.mp4", Settings = CreateDummySettings("2") };

        queue.Enqueue(job1);
        queue.Enqueue(job2);

        int cancelledCount = 0;
        queue.JobCancelled += (_, j) =>
        {
            // Accessing queue.Jobs from inside event handler
            var currentJobs = queue.Jobs;
            Assert.NotEmpty(currentJobs);
            cancelledCount++;
        };

        // This must not deadlock
        queue.CancelAll();

        Assert.Equal(2, cancelledCount);
    }
}
