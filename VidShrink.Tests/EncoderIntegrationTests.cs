using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VidShrink.Core.Models;
using VidShrink.Core.Services;
using Xunit;

namespace VidShrink.Tests;

public class EncoderIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string? _ffmpegPath;
    private readonly string? _ffprobePath;
    private readonly bool _hasFfmpeg;

    public EncoderIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"VidShrinkIntegrationTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var locator = new FfmpegLocator();
        _ffmpegPath = locator.FindExecutable("ffmpeg");
        _ffprobePath = locator.FindExecutable("ffprobe");

        _hasFfmpeg = !string.IsNullOrEmpty(_ffmpegPath) && File.Exists(_ffmpegPath);
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

    private bool SkipIfNoFfmpeg()
    {
        // Skip in headless CI runners to avoid missing GPU or platform codecs differences
        if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!_hasFfmpeg)
        {
            return true;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _ffmpegPath!,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add("lavfi");
            psi.ArgumentList.Add("-i");
            psi.ArgumentList.Add("testsrc=duration=1:size=64x64:rate=10");
            psi.ArgumentList.Add("-c:v");
            psi.ArgumentList.Add("libx264");
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add("null");
            psi.ArgumentList.Add("-");

            using var process = Process.Start(psi);
            if (process == null) return true;
            if (!process.WaitForExit(4000))
            {
                process.Kill(entireProcessTree: true);
                return true;
            }
            return process.ExitCode != 0;
        }
        catch
        {
            return true;
        }
    }

    private async Task<string> CreateSyntheticVideoAsync(string filename, int durationSeconds = 2)
    {
        var outputPath = Path.Combine(_tempDir, filename);

        var psi = new ProcessStartInfo
        {
            FileName = _ffmpegPath!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("lavfi");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add($"testsrc=duration={durationSeconds}:size=320x240:rate=30");
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("lavfi");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add($"sine=frequency=1000:duration={durationSeconds}");
        psi.ArgumentList.Add("-c:v");
        psi.ArgumentList.Add("libx264");
        psi.ArgumentList.Add("-preset");
        psi.ArgumentList.Add("ultrafast");
        psi.ArgumentList.Add("-c:a");
        psi.ArgumentList.Add("aac");
        psi.ArgumentList.Add(outputPath);

        using var process = new Process { StartInfo = psi };
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await Task.WhenAll(stdoutTask, stderrTask);

        if (!File.Exists(outputPath))
        {
            throw new InvalidOperationException("Failed to create synthetic test video with FFmpeg.");
        }

        return outputPath;
    }

    [Fact]
    public async Task EncodeAsync_RealSinglePassFfmpeg_ProducesPlayableVideo()
    {
        if (SkipIfNoFfmpeg()) return;

        var inputVideo = await CreateSyntheticVideoAsync("synth_input.mp4", durationSeconds: 2);
        var outputVideo = Path.Combine(_tempDir, "synth_output.mp4");

        var encoder = new EncoderService();
        var progressReceived = false;
        var progress = new Progress<ProgressInfo>(p =>
        {
            if (p.Percent > 0) progressReceived = true;
        });

        var settings = new CompressionSettings
        {
            InputFilePath = inputVideo,
            OutputFilePath = outputVideo,
            Duration = TimeSpan.FromSeconds(2),
            Mode = CompressionMode.Quality,
            Crf = 28,
            Speed = SpeedPreset.UltraFast,
            TwoPass = false
        };

        var result = await encoder.EncodeAsync(settings, progress);

        Assert.True(result.Success);
        Assert.True(File.Exists(outputVideo));
        Assert.True(result.OutputSizeBytes > 0);
        Assert.True(progressReceived);

        // Verify with ProbeService if ffprobe is available
        if (!string.IsNullOrEmpty(_ffprobePath) && File.Exists(_ffprobePath))
        {
            var probe = new ProbeService();
            var info = await probe.ProbeAsync(outputVideo);

            Assert.True(info.Duration.TotalSeconds > 1.5);
            Assert.Equal(320, info.Width);
            Assert.Equal(240, info.Height);
            Assert.Equal("h264", info.VideoCodec);
        }
    }

    [Fact]
    public async Task EncodeAsync_RealTwoPassLibx264_ExecutesBothPassesAndCleansLogs()
    {
        if (SkipIfNoFfmpeg()) return;

        var inputVideo = await CreateSyntheticVideoAsync("synth_2pass_input.mp4", durationSeconds: 3);
        var outputVideo = Path.Combine(_tempDir, "synth_2pass_output.mp4");

        var encoder = new EncoderService();
        int maxPassSeen = 0;
        var progress = new Progress<ProgressInfo>(p =>
        {
            if (p.CurrentPass > maxPassSeen)
                maxPassSeen = p.CurrentPass;
        });

        var settings = new CompressionSettings
        {
            InputFilePath = inputVideo,
            OutputFilePath = outputVideo,
            Duration = TimeSpan.FromSeconds(3),
            Mode = CompressionMode.TargetSize,
            TargetSizeMb = 0.5,
            Speed = SpeedPreset.UltraFast,
            TwoPass = true
        };

        var result = await encoder.EncodeAsync(settings, progress);

        Assert.True(result.Success);
        Assert.True(File.Exists(outputVideo));
        Assert.Equal(2, maxPassSeen); // Verified both Pass 1 and Pass 2 ran!

        // Verify no leftover pass logs in temp dir
        var leftoverLogs = Directory.GetFiles(_tempDir, "vidshrink2pass*");
        Assert.Empty(leftoverLogs);
    }

    [Fact]
    public async Task HardwareDetector_WithRealFfmpeg_DetectsSoftwareEncoders()
    {
        if (SkipIfNoFfmpeg()) return;

        var detector = new HardwareDetector();
        var encoders = await detector.DetectAvailableEncodersAsync(verifyWithTestEncode: false);

        Assert.NotEmpty(encoders);
        Assert.Contains(encoders, e => e.Name == "libx264");
    }

    [Fact]
    public async Task HardwareDetector_WithVerifyTestEncode_RunsRealTestEncodeWithoutHanging()
    {
        if (SkipIfNoFfmpeg()) return;

        var detector = new HardwareDetector();
        var encoders = await detector.DetectAvailableEncodersAsync(verifyWithTestEncode: true);

        Assert.NotEmpty(encoders);
        Assert.Contains(encoders, e => e.Name == "libx264");
    }
}
