using System.Linq;
using VidShrink.Core.Models;
using VidShrink.Core.Platform;
using VidShrink.Core.Services;
using Xunit;

namespace VidShrink.Tests;

public class PlatformAndDetectionTests
{
    private const string SampleFfmpegEncodersOutput = """
    Encoders:
     V..... = Video
     A..... = Audio
     S..... = Subtitle
     .F.... = Frame-level multithreading
     ..S... = Slice-level multithreading
     ...X.. = Experimental
     ....B. = Supports draw_horiz_band
     .....D = Direct rendering method
     ------
     V..... libx264              libx264 H.264 / AVC / MPEG-4 AVC / MPEG-4 part 10 (codec h264)
     V..... libx265              libx265 H.265 / HEVC (codec hevc)
     V..... libsvtav1            SVT-AV1(Scalable Video Technology for AV1) encoder (codec av1)
     V..... libvpx-vp9           libvpx VP9 (codec vp9)
     V..... h264_nvenc           NVIDIA NVENC H.264 encoder (codec h264)
     V..... hevc_nvenc           NVIDIA NVENC hevc encoder (codec hevc)
     V..... av1_nvenc            NVIDIA NVENC av1 encoder (codec av1)
     V..... h264_qsv             H.264 / AVC / MPEG-4 AVC / MPEG-4 part 10 (Intel Quick Sync Video acceleration) (codec h264)
     V..... hevc_qsv             HEVC (Intel Quick Sync Video acceleration) (codec hevc)
     V..... h264_amf             AMD AMF H.264 Encoder (codec h264)
     V..... hevc_amf             AMD AMF HEVC Encoder (codec hevc)
     V..... h264_vaapi           H.264/AVC (VAAPI) (codec h264)
     V..... hevc_vaapi           H.265/HEVC (VAAPI) (codec hevc)
    """;

    [Fact]
    public void PlatformService_Windows_ReturnsExpectedDefaults()
    {
        var platform = new TestPlatformService { CurrentPlatform = PlatformKind.Windows };

        Assert.Equal("NUL", platform.NullDevice);
        Assert.Equal("ffmpeg.exe", platform.GetExecutableName("ffmpeg"));
        Assert.Equal("ffprobe.exe", platform.GetExecutableName("ffprobe"));
        Assert.Equal("explorer", platform.GetOpenFolderCommand());

        var args = platform.GetOpenFolderArguments("C:\\test\\video.mp4");
        Assert.Contains("/select,C:\\test\\video.mp4", args);
    }

    [Fact]
    public void PlatformService_Linux_ReturnsExpectedDefaults()
    {
        var platform = new TestPlatformService { CurrentPlatform = PlatformKind.Linux };

        Assert.Equal("/dev/null", platform.NullDevice);
        Assert.Equal("ffmpeg", platform.GetExecutableName("ffmpeg"));
        Assert.Equal("ffprobe", platform.GetExecutableName("ffprobe"));
        Assert.Equal("xdg-open", platform.GetOpenFolderCommand());

        var args = platform.GetOpenFolderArguments("/home/user/videos/video.mp4");
        Assert.Contains("/home/user/videos/video.mp4", args);
    }

    [Fact]
    public void EncoderDetector_WindowsPlatform_IncludesNvencQsvAmfButExcludesVaapi()
    {
        var platform = new TestPlatformService { CurrentPlatform = PlatformKind.Windows };
        var detector = new EncoderDetector(platformService: platform);

        var encoders = detector.ParseEncodersOutput(SampleFfmpegEncodersOutput, PlatformKind.Windows);

        var names = encoders.Select(e => e.Name).ToHashSet();

        // Software
        Assert.Contains("libx264", names);
        Assert.Contains("libx265", names);
        Assert.Contains("libsvtav1", names);
        Assert.Contains("libvpx-vp9", names);

        // Windows hardware
        Assert.Contains("h264_nvenc", names);
        Assert.Contains("hevc_nvenc", names);
        Assert.Contains("h264_qsv", names);
        Assert.Contains("h264_amf", names);

        // Linux VAAPI must NOT be included on Windows!
        Assert.DoesNotContain("h264_vaapi", names);
        Assert.DoesNotContain("hevc_vaapi", names);
    }

    [Fact]
    public void EncoderDetector_LinuxPlatform_IncludesNvencAndVaapiButExcludesAmf()
    {
        var platform = new TestPlatformService { CurrentPlatform = PlatformKind.Linux };
        var detector = new EncoderDetector(platformService: platform);

        var encoders = detector.ParseEncodersOutput(SampleFfmpegEncodersOutput, PlatformKind.Linux);

        var names = encoders.Select(e => e.Name).ToHashSet();

        // Software
        Assert.Contains("libx264", names);
        Assert.Contains("libx265", names);

        // Linux hardware
        Assert.Contains("h264_nvenc", names);
        Assert.Contains("h264_vaapi", names);
        Assert.Contains("hevc_vaapi", names);

        // Windows AMF must NOT be included on Linux!
        Assert.DoesNotContain("h264_amf", names);
        Assert.DoesNotContain("hevc_amf", names);
    }

    [Fact]
    public void EncoderDetector_EmptyOutput_ReturnsEmptyList()
    {
        var detector = new EncoderDetector();
        var encoders = detector.ParseEncodersOutput(string.Empty, PlatformKind.Windows);

        Assert.Empty(encoders);
    }

    [Fact]
    public async Task EncoderDetector_DetectAvailableEncodersAsync_FiltersFailedHardwareEncoders()
    {
        var platform = new TestPlatformService { CurrentPlatform = PlatformKind.Windows };
        var locator = new MockFfmpegLocator();
        var mockRunner = new MockProcessRunner();

        mockRunner.OnRunAsync = (exe, args, stdout, stderr, token) =>
        {
            if (args.Contains("-encoders"))
            {
                stdout?.Invoke(SampleFfmpegEncodersOutput);
                return Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardError = "", WasCancelled = false });
            }

            // Test encode: NVENC succeeds, AMF fails
            if (args.Contains("h264_nvenc"))
            {
                return Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardError = "", WasCancelled = false });
            }

            return Task.FromResult(new ProcessExecutionResult { ExitCode = 1, StandardError = "AMF not supported", WasCancelled = false });
        };

        var detector = new EncoderDetector(locator, platform, mockRunner);
        var encoders = await detector.DetectAvailableEncodersAsync(verifyWithTestEncode: true);

        var names = encoders.Select(e => e.Name).ToList();

        // Software always included
        Assert.Contains("libx264", names);
        // Succeeded hardware included
        Assert.Contains("h264_nvenc", names);
        // Failed hardware excluded
        Assert.DoesNotContain("h264_amf", names);
    }

    [Fact]
    public async Task EncoderDetector_LinuxVaapi_UsesVaapiDeviceAndFilterInTestEncode()
    {
        var platform = new TestPlatformService { CurrentPlatform = PlatformKind.Linux };
        var locator = new MockFfmpegLocator();
        var mockRunner = new MockProcessRunner();
        var capturedArgs = new List<IReadOnlyList<string>>();

        mockRunner.OnRunAsync = (exe, args, stdout, stderr, token) =>
        {
            capturedArgs.Add(args);
            if (args.Contains("-encoders"))
            {
                stdout?.Invoke(SampleFfmpegEncodersOutput);
                return Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardError = "", WasCancelled = false });
            }

            return Task.FromResult(new ProcessExecutionResult { ExitCode = 0, StandardError = "", WasCancelled = false });
        };

        var detector = new EncoderDetector(locator, platform, mockRunner);
        var encoders = await detector.DetectAvailableEncodersAsync(verifyWithTestEncode: true);

        // Find the test encode call for h264_vaapi
        var vaapiCall = capturedArgs.FirstOrDefault(args => args.Contains("h264_vaapi"));
        Assert.NotNull(vaapiCall);

        Assert.Contains("-vaapi_device", vaapiCall);
        var devIdx = vaapiCall.IndexOf("-vaapi_device");
        Assert.Equal("/dev/dri/renderD128", vaapiCall[devIdx + 1]);

        Assert.Contains("-vf", vaapiCall);
        var vfIdx = vaapiCall.IndexOf("-vf");
        Assert.Equal("format=nv12,hwupload", vaapiCall[vfIdx + 1]);

        Assert.Contains("/dev/null", vaapiCall);
    }
}
