using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VidShrink.Core.Models;
using VidShrink.Core.Platform;
using VidShrink.Core.Services;
using Xunit;

namespace VidShrink.Tests;

public class MockProcessRunner : IProcessRunner
{
    public List<IReadOnlyList<string>> ExecutedArguments { get; } = new();

    public Func<string, IReadOnlyList<string>, Action<string>?, Action<string>?, CancellationToken, Task<ProcessExecutionResult>>? OnRunAsync { get; set; }

    public Task<ProcessExecutionResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        Action<string>? onStandardOutputLine = null,
        Action<string>? onStandardErrorLine = null,
        CancellationToken cancellationToken = default)
    {
        ExecutedArguments.Add(arguments);

        if (OnRunAsync != null)
        {
            return OnRunAsync(executable, arguments, onStandardOutputLine, onStandardErrorLine, cancellationToken);
        }

        return Task.FromResult(new ProcessExecutionResult
        {
            ExitCode = 0,
            StandardError = string.Empty,
            WasCancelled = false
        });
    }
}

public class MockFfmpegLocator : IFfmpegLocator
{
    public string? FfmpegPath { get; set; } = "C:\\mock\\ffmpeg.exe";
    public string? FfprobePath { get; set; } = "C:\\mock\\ffprobe.exe";

    public string? FindExecutable(string baseName, string? customDirectory = null)
    {
        if (baseName.Contains("probe", StringComparison.OrdinalIgnoreCase))
            return FfprobePath;
        return FfmpegPath;
    }

    public FfmpegPaths Locate(string? customFfmpegDirectory = null)
    {
        return new FfmpegPaths
        {
            FfmpegPath = FfmpegPath,
            FfprobePath = FfprobePath
        };
    }

    public string? GetVersion(string executablePath) => "mock-8.0";
}

public class EncoderServiceTests : IDisposable
{
    private readonly string _testDir;

    public EncoderServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"VidShrinkEncoderTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task EncodeAsync_InjectsProgressArgumentsAndRunsSuccessfully()
    {
        var inputPath = Path.Combine(_testDir, "input.mp4");
        var outputPath = Path.Combine(_testDir, "output.mp4");
        File.WriteAllText(inputPath, "mock input content");

        var mockRunner = new MockProcessRunner();
        mockRunner.OnRunAsync = (exe, args, stdout, stderr, token) =>
        {
            // Simulate output file creation and progress
            File.WriteAllText(outputPath, "compressed output content");
            stdout?.Invoke("out_time_us=30000000");
            stdout?.Invoke("speed=1.5x");
            stdout?.Invoke("progress=end");

            return Task.FromResult(new ProcessExecutionResult
            {
                ExitCode = 0,
                StandardError = string.Empty,
                WasCancelled = false
            });
        };

        var locator = new MockFfmpegLocator();
        var service = new EncoderService(locator: locator, processRunner: mockRunner);

        var settings = new CompressionSettings
        {
            InputFilePath = inputPath,
            OutputFilePath = outputPath,
            Duration = TimeSpan.FromSeconds(60),
            Mode = CompressionMode.Quality,
            Crf = 23,
            TwoPass = false
        };

        var progressReported = false;
        var progress = new Progress<ProgressInfo>(_ => progressReported = true);

        var result = await service.EncodeAsync(settings, progress);

        for (int i = 0; i < 50 && !progressReported; i++)
        {
            await Task.Delay(10);
        }

        Assert.True(result.Success);
        Assert.Equal(outputPath, result.OutputFilePath);
        Assert.True(File.Exists(outputPath));
        Assert.True(progressReported);

        // Verify arguments included -progress pipe:1 -nostats
        Assert.Single(mockRunner.ExecutedArguments);
        var args = mockRunner.ExecutedArguments[0];
        Assert.Contains("-progress", args);
        Assert.Contains("pipe:1", args);
        Assert.Contains("-nostats", args);
    }

    [Fact]
    public async Task EncodeAsync_OversizedOutput_TriggersAutoRetryWithLowerBitrate()
    {
        var inputPath = Path.Combine(_testDir, "input.mp4");
        var outputPath = Path.Combine(_testDir, "output.mp4");
        File.WriteAllText(inputPath, "mock input content");

        var mockRunner = new MockProcessRunner();
        int runCount = 0;

        mockRunner.OnRunAsync = (exe, args, stdout, stderr, token) =>
        {
            runCount++;
            if (runCount == 1)
            {
                // First run: output size exceeds 10 MB (12 MB)
                var oversizedBytes = new byte[12 * 1024 * 1024];
                File.WriteAllBytes(outputPath, oversizedBytes);
            }
            else
            {
                // Second run (retry): output size within limit (8 MB)
                var validBytes = new byte[8 * 1024 * 1024];
                File.WriteAllBytes(outputPath, validBytes);
            }

            stdout?.Invoke("progress=end");
            return Task.FromResult(new ProcessExecutionResult
            {
                ExitCode = 0,
                StandardError = string.Empty,
                WasCancelled = false
            });
        };

        var locator = new MockFfmpegLocator();
        var service = new EncoderService(locator: locator, processRunner: mockRunner);

        var warningMessage = string.Empty;
        service.MessageLogged += (_, msg) => warningMessage = msg;

        var settings = new CompressionSettings
        {
            InputFilePath = inputPath,
            OutputFilePath = outputPath,
            Duration = TimeSpan.FromSeconds(60),
            Mode = CompressionMode.TargetSize,
            TargetSizeMb = 10.0,
            TwoPass = false
        };

        var result = await service.EncodeAsync(settings);

        Assert.True(result.Success);
        Assert.True(result.RetriedWithLowerBitrate);
        Assert.Equal(2, runCount); // Confirms retry ran
        Assert.Contains("x0.92", warningMessage);
        Assert.Equal(8 * 1024 * 1024, result.OutputSizeBytes);

        Assert.Equal(2, mockRunner.ExecutedArguments.Count);
        var run1Args = mockRunner.ExecutedArguments[0];
        var run2Args = mockRunner.ExecutedArguments[1];
        var bIdx1 = run1Args.IndexOf("-b:v");
        var bIdx2 = run2Args.IndexOf("-b:v");
        Assert.True(bIdx1 >= 0);
        Assert.True(bIdx2 >= 0);
        Assert.Equal("1228k", run1Args[bIdx1 + 1]);
        Assert.Equal("1129k", run2Args[bIdx2 + 1]); // 1228 * 0.92 = 1129k
    }

    [Fact]
    public async Task EncodeAsync_CancellationRequested_DeletesIncompleteOutputAndThrows()
    {
        var inputPath = Path.Combine(_testDir, "input.mp4");
        var outputPath = Path.Combine(_testDir, "output.mp4");
        File.WriteAllText(inputPath, "mock input");

        var mockRunner = new MockProcessRunner();
        mockRunner.OnRunAsync = (exe, args, stdout, stderr, token) =>
        {
            // Create incomplete output before cancelling
            File.WriteAllText(outputPath, "incomplete data");
            return Task.FromResult(new ProcessExecutionResult
            {
                ExitCode = -1,
                StandardError = string.Empty,
                WasCancelled = true
            });
        };

        var locator = new MockFfmpegLocator();
        var service = new EncoderService(locator: locator, processRunner: mockRunner);

        var settings = new CompressionSettings
        {
            InputFilePath = inputPath,
            OutputFilePath = outputPath,
            Duration = TimeSpan.FromSeconds(30),
            TwoPass = false
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Immediately cancel

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.EncodeAsync(settings, cancellationToken: cts.Token));

        // Incomplete output must be cleaned up
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task EncodeAsync_FfmpegFailure_CleansUpOutputAndThrowsEncoderException()
    {
        var inputPath = Path.Combine(_testDir, "input.mp4");
        var outputPath = Path.Combine(_testDir, "output.mp4");
        File.WriteAllText(inputPath, "mock input");

        var mockRunner = new MockProcessRunner();
        mockRunner.OnRunAsync = (exe, args, stdout, stderr, token) =>
        {
            File.WriteAllText(outputPath, "partial failure data");
            return Task.FromResult(new ProcessExecutionResult
            {
                ExitCode = 1,
                StandardError = "Error: Invalid argument in video stream filter\nConversion failed!",
                WasCancelled = false
            });
        };

        var locator = new MockFfmpegLocator();
        var service = new EncoderService(locator: locator, processRunner: mockRunner);

        var settings = new CompressionSettings
        {
            InputFilePath = inputPath,
            OutputFilePath = outputPath,
            Duration = TimeSpan.FromSeconds(30),
            TwoPass = false
        };

        var ex = await Assert.ThrowsAsync<EncoderException>(() => service.EncodeAsync(settings));
        Assert.Equal(1, ex.ExitCode);
        Assert.Contains("Conversion failed!", ex.Message);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public async Task EncodeAsync_TwoPassEncoding_CleansUpPassLogFiles()
    {
        var inputPath = Path.Combine(_testDir, "input.mp4");
        var outputPath = Path.Combine(_testDir, "output.mp4");
        File.WriteAllText(inputPath, "mock input");

        string? capturedLogPrefix = null;

        var mockRunner = new MockProcessRunner();
        mockRunner.OnRunAsync = (exe, args, stdout, stderr, token) =>
        {
            // Locate pass log prefix from arguments
            for (int i = 0; i < args.Count; i++)
            {
                if (args[i] == "-passlogfile" && i + 1 < args.Count)
                {
                    capturedLogPrefix = args[i + 1];
                    // Create simulated pass logs
                    File.WriteAllText($"{capturedLogPrefix}-0.log", "pass 1 stats");
                    File.WriteAllText($"{capturedLogPrefix}-0.log.mbtree", "mbtree data");
                }
            }

            File.WriteAllText(outputPath, "pass 2 finished data");
            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardError = string.Empty, WasCancelled = false });
        };

        var locator = new MockFfmpegLocator();
        var service = new EncoderService(locator: locator, processRunner: mockRunner);

        var settings = new CompressionSettings
        {
            InputFilePath = inputPath,
            OutputFilePath = outputPath,
            Duration = TimeSpan.FromSeconds(60),
            Mode = CompressionMode.TargetSize,
            TargetSizeMb = 10,
            VideoCodec = VideoCodec.H264,
            HardwareAcceleration = HardwareAcceleration.None,
            TwoPass = true
        };

        var result = await service.EncodeAsync(settings);

        Assert.True(result.Success);
        Assert.NotNull(capturedLogPrefix);
        // Pass log files should be cleaned up!
        Assert.False(File.Exists($"{capturedLogPrefix}-0.log"));
        Assert.False(File.Exists($"{capturedLogPrefix}-0.log.mbtree"));
    }
}
