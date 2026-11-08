using System;
using System.IO;
using VidShrink.Core.Models;
using VidShrink.Core.Platform;
using VidShrink.Core.Services;
using Xunit;

using System.Collections.Generic;

namespace VidShrink.Tests;

internal static class ListExtensions
{
    public static int IndexOf(this IReadOnlyList<string> list, string item)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (string.Equals(list[i], item, StringComparison.Ordinal))
                return i;
        }
        return -1;
    }
}

public class CommandBuilderTests
{
    private readonly TestPlatformService _windowsPlatform = new() { CurrentPlatform = PlatformKind.Windows };
    private readonly TestPlatformService _linuxPlatform = new() { CurrentPlatform = PlatformKind.Linux };

    [Fact]
    public void BuildPlan_SoftwareH264TwoPass_WindowsUsesNulDevice()
    {
        var builder = new CommandBuilder(_windowsPlatform);
        var settings = new CompressionSettings
        {
            InputFilePath = "input.mp4",
            OutputFilePath = "output.mp4",
            Mode = CompressionMode.TargetSize,
            TargetSizeMb = 8.0,
            Duration = TimeSpan.FromSeconds(60),
            VideoCodec = VideoCodec.H264,
            HardwareAcceleration = HardwareAcceleration.None,
            TwoPass = true
        };

        var plan = builder.BuildPlan(settings, passLogPrefix: "testlog");

        Assert.Equal(2, plan.Commands.Count);

        // Pass 1
        var pass1 = plan.Commands[0];
        Assert.Equal("NUL", pass1.OutputPath);
        Assert.Contains("NUL", pass1.Arguments);
        Assert.Contains("-pass", pass1.Arguments);
        Assert.Equal("1", pass1.Arguments[pass1.Arguments.IndexOf("-pass") + 1]);
        Assert.Contains("-an", pass1.Arguments);
        Assert.Contains("-f", pass1.Arguments);
        Assert.Equal("null", pass1.Arguments[pass1.Arguments.IndexOf("-f") + 1]);
        Assert.Contains("-c:v", pass1.Arguments);
        Assert.Equal("libx264", pass1.Arguments[pass1.Arguments.IndexOf("-c:v") + 1]);

        // Pass 2
        var pass2 = plan.Commands[1];
        Assert.Equal("output.mp4", pass2.OutputPath);
        Assert.Contains("-pass", pass2.Arguments);
        Assert.Equal("2", pass2.Arguments[pass2.Arguments.IndexOf("-pass") + 1]);
        Assert.Contains("-c:a", pass2.Arguments);
        Assert.Equal("aac", pass2.Arguments[pass2.Arguments.IndexOf("-c:a") + 1]);
        Assert.Contains("-movflags", pass2.Arguments);
        Assert.Contains("+faststart", pass2.Arguments);
    }

    [Fact]
    public void BuildPlan_SoftwareH264TwoPass_LinuxUsesDevNullDevice()
    {
        var builder = new CommandBuilder(_linuxPlatform);
        var settings = new CompressionSettings
        {
            InputFilePath = "input.mp4",
            OutputFilePath = "output.mp4",
            Mode = CompressionMode.TargetSize,
            TargetSizeMb = 25.0,
            Duration = TimeSpan.FromSeconds(120),
            VideoCodec = VideoCodec.H264,
            HardwareAcceleration = HardwareAcceleration.None,
            TwoPass = true
        };

        var plan = builder.BuildPlan(settings, passLogPrefix: "/tmp/vidshrink2pass");

        Assert.Equal(2, plan.Commands.Count);
        var pass1 = plan.Commands[0];
        Assert.Equal("/dev/null", pass1.OutputPath);
        Assert.Contains("/dev/null", pass1.Arguments);
    }

    [Fact]
    public void BuildPlan_VaapiLinux_AddsVaapiDeviceAndHwuploadFilter()
    {
        var builder = new CommandBuilder(_linuxPlatform);
        var settings = new CompressionSettings
        {
            InputFilePath = "input.mp4",
            OutputFilePath = "output.mp4",
            Mode = CompressionMode.TargetSize,
            TargetSizeMb = 10.0,
            Duration = TimeSpan.FromSeconds(30),
            VideoCodec = VideoCodec.H264,
            HardwareAcceleration = HardwareAcceleration.Vaapi,
            VaapiDevice = "/dev/dri/renderD128",
            TwoPass = false
        };

        var plan = builder.BuildPlan(settings);

        Assert.Single(plan.Commands);
        var cmd = plan.Commands[0];

        // Check -vaapi_device
        Assert.Contains("-vaapi_device", cmd.Arguments);
        Assert.Equal("/dev/dri/renderD128", cmd.Arguments[cmd.Arguments.IndexOf("-vaapi_device") + 1]);

        // Check encoder
        Assert.Contains("-c:v", cmd.Arguments);
        Assert.Equal("h264_vaapi", cmd.Arguments[cmd.Arguments.IndexOf("-c:v") + 1]);

        // Check vf hwupload
        Assert.Contains("-vf", cmd.Arguments);
        var vfIndex = cmd.Arguments.IndexOf("-vf") + 1;
        var vf = cmd.Arguments[vfIndex];
        Assert.Contains("format=nv12", vf);
        Assert.Contains("hwupload", vf);
    }

    [Fact]
    public void BuildPlan_VaapiWithScaling_CombinesScaleAndHwupload()
    {
        var builder = new CommandBuilder(_linuxPlatform);
        var settings = new CompressionSettings
        {
            InputFilePath = "input.mp4",
            OutputFilePath = "output.mp4",
            Mode = CompressionMode.Quality,
            Crf = 24,
            VideoCodec = VideoCodec.H265,
            HardwareAcceleration = HardwareAcceleration.Vaapi,
            OutputWidth = 1280,
            TwoPass = false
        };

        var plan = builder.BuildPlan(settings);
        var cmd = plan.Commands[0];

        Assert.Contains("-c:v", cmd.Arguments);
        Assert.Equal("hevc_vaapi", cmd.Arguments[cmd.Arguments.IndexOf("-c:v") + 1]);

        Assert.Contains("-vf", cmd.Arguments);
        var vf = cmd.Arguments[cmd.Arguments.IndexOf("-vf") + 1];
        Assert.Contains("scale=1280:-2", vf);
        Assert.Contains("format=nv12", vf);
        Assert.Contains("hwupload", vf);
    }

    [Theory]
    [InlineData(VideoCodec.H264, HardwareAcceleration.None, "libx264")]
    [InlineData(VideoCodec.H265, HardwareAcceleration.None, "libx265")]
    [InlineData(VideoCodec.AV1, HardwareAcceleration.None, "libsvtav1")]
    [InlineData(VideoCodec.VP9, HardwareAcceleration.None, "libvpx-vp9")]
    [InlineData(VideoCodec.H264, HardwareAcceleration.Nvenc, "h264_nvenc")]
    [InlineData(VideoCodec.H265, HardwareAcceleration.Nvenc, "hevc_nvenc")]
    [InlineData(VideoCodec.AV1, HardwareAcceleration.Nvenc, "av1_nvenc")]
    [InlineData(VideoCodec.H264, HardwareAcceleration.Qsv, "h264_qsv")]
    [InlineData(VideoCodec.H265, HardwareAcceleration.Qsv, "hevc_qsv")]
    [InlineData(VideoCodec.H264, HardwareAcceleration.Amf, "h264_amf")]
    [InlineData(VideoCodec.H264, HardwareAcceleration.Vaapi, "h264_vaapi")]
    [InlineData(VideoCodec.H265, HardwareAcceleration.Vaapi, "hevc_vaapi")]
    public void ResolveEncoder_AllCodecsAndAccelerations_ReturnsExpectedEncoderName(
        VideoCodec codec,
        HardwareAcceleration accel,
        string expectedEncoder)
    {
        var builder = new CommandBuilder(_windowsPlatform);
        var resolved = builder.ResolveEncoder(codec, accel);
        Assert.Equal(expectedEncoder, resolved);
    }

    [Fact]
    public void BuildPlan_QualityMode_SetsAppropriateCrfAndQpFlags()
    {
        var builder = new CommandBuilder(_windowsPlatform);

        // x264 CRF
        var x264Settings = new CompressionSettings
        {
            InputFilePath = "in.mp4",
            OutputFilePath = "out.mp4",
            Mode = CompressionMode.Quality,
            Crf = 22,
            VideoCodec = VideoCodec.H264,
            HardwareAcceleration = HardwareAcceleration.None
        };
        var x264Plan = builder.BuildPlan(x264Settings);
        Assert.Contains("-crf", x264Plan.Commands[0].Arguments);
        Assert.Equal("22", x264Plan.Commands[0].Arguments[x264Plan.Commands[0].Arguments.IndexOf("-crf") + 1]);

        // NVENC CQ
        var nvencSettings = new CompressionSettings
        {
            InputFilePath = "in.mp4",
            OutputFilePath = "out.mp4",
            Mode = CompressionMode.Quality,
            Crf = 26,
            VideoCodec = VideoCodec.H264,
            HardwareAcceleration = HardwareAcceleration.Nvenc
        };
        var nvencPlan = builder.BuildPlan(nvencSettings);
        Assert.Contains("-cq", nvencPlan.Commands[0].Arguments);
        Assert.Equal("26", nvencPlan.Commands[0].Arguments[nvencPlan.Commands[0].Arguments.IndexOf("-cq") + 1]);

        // VAAPI QP
        var vaapiSettings = new CompressionSettings
        {
            InputFilePath = "in.mp4",
            OutputFilePath = "out.mp4",
            Mode = CompressionMode.Quality,
            Crf = 24,
            VideoCodec = VideoCodec.H264,
            HardwareAcceleration = HardwareAcceleration.Vaapi
        };
        var vaapiPlan = builder.BuildPlan(vaapiSettings);
        Assert.Contains("-qp", vaapiPlan.Commands[0].Arguments);
        Assert.Equal("24", vaapiPlan.Commands[0].Arguments[vaapiPlan.Commands[0].Arguments.IndexOf("-qp") + 1]);
    }

    [Fact]
    public void BuildPlan_AudioModes_FormatsArgumentsCorrectly()
    {
        var builder = new CommandBuilder(_windowsPlatform);

        // Opus
        var opusPlan = builder.BuildPlan(new CompressionSettings
        {
            InputFilePath = "in.mp4",
            OutputFilePath = "out.mkv",
            Mode = CompressionMode.Quality,
            AudioMode = AudioMode.Opus,
            AudioBitrateKbps = 96
        });
        Assert.Contains("-c:a", opusPlan.Commands[0].Arguments);
        Assert.Equal("libopus", opusPlan.Commands[0].Arguments[opusPlan.Commands[0].Arguments.IndexOf("-c:a") + 1]);
        Assert.Equal("96k", opusPlan.Commands[0].Arguments[opusPlan.Commands[0].Arguments.IndexOf("-b:a") + 1]);

        // Copy
        var copyPlan = builder.BuildPlan(new CompressionSettings
        {
            InputFilePath = "in.mp4",
            OutputFilePath = "out.mp4",
            Mode = CompressionMode.Quality,
            AudioMode = AudioMode.Copy
        });
        Assert.Contains("-c:a", copyPlan.Commands[0].Arguments);
        Assert.Equal("copy", copyPlan.Commands[0].Arguments[copyPlan.Commands[0].Arguments.IndexOf("-c:a") + 1]);

        // Mute
        var mutePlan = builder.BuildPlan(new CompressionSettings
        {
            InputFilePath = "in.mp4",
            OutputFilePath = "out.mp4",
            Mode = CompressionMode.Quality,
            AudioMode = AudioMode.Mute
        });
        Assert.Contains("-an", mutePlan.Commands[0].Arguments);
    }

    [Fact]
    public void BuildPlan_SoftwareScaling_AddsCorrectScaleFilter()
    {
        var builder = new CommandBuilder(_windowsPlatform);

        var settings = new CompressionSettings
        {
            InputFilePath = "in.mp4",
            OutputFilePath = "out.mp4",
            Mode = CompressionMode.Quality,
            OutputWidth = 1920,
            OutputHeight = 1080
        };

        var plan = builder.BuildPlan(settings);
        var cmd = plan.Commands[0];

        Assert.Contains("-vf", cmd.Arguments);
        var vf = cmd.Arguments[cmd.Arguments.IndexOf("-vf") + 1];
        Assert.Contains("scale='min(1920,iw)':", vf);
    }

    [Fact]
    public void BuildPlan_SoftwareH265TwoPass_UsesX265ParamsPassAndStats()
    {
        var builder = new CommandBuilder(_windowsPlatform);
        var settings = new CompressionSettings
        {
            InputFilePath = "input.mp4",
            OutputFilePath = "output.mp4",
            Mode = CompressionMode.TargetSize,
            TargetSizeMb = 15.0,
            Duration = TimeSpan.FromSeconds(60),
            VideoCodec = VideoCodec.H265,
            HardwareAcceleration = HardwareAcceleration.None,
            TwoPass = true
        };

        var plan = builder.BuildPlan(settings, passLogPrefix: "x265_stats");

        Assert.Equal(2, plan.Commands.Count);

        // Pass 1
        var pass1 = plan.Commands[0];
        Assert.Contains("-x265-params", pass1.Arguments);
        Assert.Equal("pass=1:stats=x265_stats", pass1.Arguments[pass1.Arguments.IndexOf("-x265-params") + 1]);
        Assert.Contains("-an", pass1.Arguments);

        // Pass 2
        var pass2 = plan.Commands[1];
        Assert.Contains("-x265-params", pass2.Arguments);
        Assert.Equal("pass=2:stats=x265_stats", pass2.Arguments[pass2.Arguments.IndexOf("-x265-params") + 1]);
        Assert.Contains("-tag:v", pass2.Arguments);
        Assert.Equal("hvc1", pass2.Arguments[pass2.Arguments.IndexOf("-tag:v") + 1]);
    }

    [Fact]
    public void BuildPlan_SvtAv1TargetSize_UsesSinglePassWithBitrateBuffer()
    {
        var builder = new CommandBuilder(_windowsPlatform);
        var settings = new CompressionSettings
        {
            InputFilePath = "input.mp4",
            OutputFilePath = "output.mp4",
            Mode = CompressionMode.TargetSize,
            TargetSizeMb = 10.0,
            Duration = TimeSpan.FromSeconds(60),
            VideoCodec = VideoCodec.AV1,
            HardwareAcceleration = HardwareAcceleration.None,
            TwoPass = true // Requesting two pass should be downgraded to single pass for SVT-AV1
        };

        var plan = builder.BuildPlan(settings);

        Assert.Single(plan.Commands);
        var cmd = plan.Commands[0];
        Assert.Contains("-b:v", cmd.Arguments);
        Assert.Contains("-maxrate", cmd.Arguments);
        Assert.Contains("-bufsize", cmd.Arguments);
        Assert.DoesNotContain("-pass", cmd.Arguments);
    }

    [Fact]
    public void BuildPlan_HardwareNvencTargetSize_UsesSinglePassWithBitrateBuffer()
    {
        var builder = new CommandBuilder(_windowsPlatform);
        var settings = new CompressionSettings
        {
            InputFilePath = "input.mp4",
            OutputFilePath = "output.mp4",
            Mode = CompressionMode.TargetSize,
            TargetSizeMb = 25.0,
            Duration = TimeSpan.FromSeconds(120),
            VideoCodec = VideoCodec.H264,
            HardwareAcceleration = HardwareAcceleration.Nvenc,
            TwoPass = true // Hardware should remain single-pass
        };

        var plan = builder.BuildPlan(settings);

        Assert.Single(plan.Commands);
        var cmd = plan.Commands[0];
        Assert.Equal("h264_nvenc", cmd.Arguments[cmd.Arguments.IndexOf("-c:v") + 1]);
        Assert.Contains("-b:v", cmd.Arguments);
        Assert.Contains("-maxrate", cmd.Arguments);
        Assert.Contains("-bufsize", cmd.Arguments);
        Assert.DoesNotContain("-pass", cmd.Arguments);
    }

    [Fact]
    public void BuildPlan_TimeTrimming_AddsSsAndToParameters()
    {
        var builder = new CommandBuilder(_windowsPlatform);
        var settings = new CompressionSettings
        {
            InputFilePath = "input.mp4",
            OutputFilePath = "output.mp4",
            Mode = CompressionMode.Quality,
            Crf = 23,
            StartTime = TimeSpan.FromSeconds(10.5),
            EndTime = TimeSpan.FromSeconds(45.0)
        };

        var plan = builder.BuildPlan(settings);
        var cmd = plan.Commands[0];

        Assert.Contains("-ss", cmd.Arguments);
        var ssIndex = cmd.Arguments.IndexOf("-ss");
        Assert.Equal("00:00:10.500", cmd.Arguments[ssIndex + 1]);

        Assert.Contains("-to", cmd.Arguments);
        var toIndex = cmd.Arguments.IndexOf("-to");
        Assert.Equal("00:00:45.000", cmd.Arguments[toIndex + 1]);

        // Trimming parameters should appear before -i for fast seek
        var inputIndex = cmd.Arguments.IndexOf("-i");
        Assert.True(ssIndex < inputIndex);
        Assert.True(toIndex < inputIndex);
    }

    [Fact]
    public void BuildPlan_FiveSpecificUserScenarios_GeneratesCorrectCommands()
    {
        var winBuilder = new CommandBuilder(_windowsPlatform);
        var linBuilder = new CommandBuilder(_linuxPlatform);

        // 1. libx264 two-pass (Windows & Linux), 60s, 10MB
        var s1 = new CompressionSettings
        {
            InputFilePath = "input.mp4",
            OutputFilePath = "output.mp4",
            Duration = TimeSpan.FromSeconds(60),
            Mode = CompressionMode.TargetSize,
            TargetSizeMb = 10,
            VideoCodec = VideoCodec.H264,
            HardwareAcceleration = HardwareAcceleration.None,
            TwoPass = true
        };
        var plan1Win = winBuilder.BuildPlan(s1, passLogPrefix: "C:\\Temp\\vidshrink2pass_log");
        var plan1Lin = linBuilder.BuildPlan(s1, passLogPrefix: "/tmp/vidshrink2pass_log");

        Assert.Equal("ffmpeg -y -hide_banner -i input.mp4 -c:v libx264 -b:v 1228k -maxrate 1842k -bufsize 2456k -pass 1 -passlogfile C:\\Temp\\vidshrink2pass_log -pix_fmt yuv420p -preset medium -an -f null NUL", plan1Win.Commands[0].ToCommandLineString());
        Assert.Equal("ffmpeg -y -hide_banner -i input.mp4 -c:v libx264 -b:v 1228k -maxrate 1842k -bufsize 2456k -pass 1 -passlogfile /tmp/vidshrink2pass_log -pix_fmt yuv420p -preset medium -an -f null /dev/null", plan1Lin.Commands[0].ToCommandLineString());
        Assert.Equal("ffmpeg -y -hide_banner -i input.mp4 -c:v libx264 -b:v 1228k -maxrate 1842k -bufsize 2456k -pass 2 -passlogfile C:\\Temp\\vidshrink2pass_log -pix_fmt yuv420p -preset medium -c:a aac -b:a 128k -movflags +faststart output.mp4", plan1Win.Commands[1].ToCommandLineString());

        // 2. libx265 two-pass (Windows & Linux), 60s, 10MB
        var s2 = new CompressionSettings
        {
            InputFilePath = "input.mp4",
            OutputFilePath = "output.mp4",
            Duration = TimeSpan.FromSeconds(60),
            Mode = CompressionMode.TargetSize,
            TargetSizeMb = 10,
            VideoCodec = VideoCodec.H265,
            HardwareAcceleration = HardwareAcceleration.None,
            TwoPass = true
        };
        var plan2Win = winBuilder.BuildPlan(s2, passLogPrefix: "C:\\Temp\\vidshrink2pass_x265");
        var plan2Lin = linBuilder.BuildPlan(s2, passLogPrefix: "/tmp/vidshrink2pass_x265");

        Assert.Equal("ffmpeg -y -hide_banner -i input.mp4 -c:v libx265 -b:v 1228k -maxrate 1842k -bufsize 2456k -x265-params pass=1:stats=C:\\Temp\\vidshrink2pass_x265 -pix_fmt yuv420p -preset medium -an -f null NUL", plan2Win.Commands[0].ToCommandLineString());
        Assert.Equal("ffmpeg -y -hide_banner -i input.mp4 -c:v libx265 -b:v 1228k -maxrate 1842k -bufsize 2456k -x265-params pass=1:stats=/tmp/vidshrink2pass_x265 -pix_fmt yuv420p -preset medium -an -f null /dev/null", plan2Lin.Commands[0].ToCommandLineString());
        Assert.Equal("ffmpeg -y -hide_banner -i input.mp4 -c:v libx265 -b:v 1228k -maxrate 1842k -bufsize 2456k -x265-params pass=2:stats=C:\\Temp\\vidshrink2pass_x265 -pix_fmt yuv420p -preset medium -c:a aac -b:a 128k -movflags +faststart -tag:v hvc1 output.mp4", plan2Win.Commands[1].ToCommandLineString());

        // 3. libsvtav1 single pass, 60s, 10MB
        var s3 = new CompressionSettings
        {
            InputFilePath = "input.mp4",
            OutputFilePath = "output.mp4",
            Duration = TimeSpan.FromSeconds(60),
            Mode = CompressionMode.TargetSize,
            TargetSizeMb = 10,
            VideoCodec = VideoCodec.AV1,
            HardwareAcceleration = HardwareAcceleration.None,
            TwoPass = true // CommandBuilder will enforce single pass for SVT-AV1
        };
        var plan3 = winBuilder.BuildPlan(s3);
        Assert.Single(plan3.Commands);
        Assert.Equal("ffmpeg -y -hide_banner -i input.mp4 -c:v libsvtav1 -b:v 1228k -maxrate 1842k -bufsize 2456k -pix_fmt yuv420p10le -preset medium -c:a aac -b:a 128k -movflags +faststart output.mp4", plan3.Commands[0].ToCommandLineString());

        // 4. h264_nvenc single pass, 60s, 10MB
        var s4 = new CompressionSettings
        {
            InputFilePath = "input.mp4",
            OutputFilePath = "output.mp4",
            Duration = TimeSpan.FromSeconds(60),
            Mode = CompressionMode.TargetSize,
            TargetSizeMb = 10,
            VideoCodec = VideoCodec.H264,
            HardwareAcceleration = HardwareAcceleration.Nvenc,
            TwoPass = true // CommandBuilder enforces single pass for NVENC
        };
        var plan4 = winBuilder.BuildPlan(s4);
        Assert.Single(plan4.Commands);
        Assert.Equal("ffmpeg -y -hide_banner -i input.mp4 -c:v h264_nvenc -b:v 1228k -maxrate 1842k -bufsize 2456k -pix_fmt yuv420p -preset p5 -c:a aac -b:a 128k -movflags +faststart output.mp4", plan4.Commands[0].ToCommandLineString());

        // 5. libx264 Quality mode CRF with scale to 720p and trim 00:10-00:40
        var s5 = new CompressionSettings
        {
            InputFilePath = "input.mp4",
            OutputFilePath = "output.mp4",
            Mode = CompressionMode.Quality,
            Crf = 23,
            VideoCodec = VideoCodec.H264,
            HardwareAcceleration = HardwareAcceleration.None,
            OutputHeight = 720,
            StartTime = TimeSpan.FromSeconds(10),
            EndTime = TimeSpan.FromSeconds(40)
        };
        var plan5 = winBuilder.BuildPlan(s5);
        Assert.Single(plan5.Commands);
        Assert.Equal("ffmpeg -y -hide_banner -ss 00:00:10.000 -to 00:00:40.000 -i input.mp4 -vf scale=-2:720 -c:v libx264 -crf 23 -pix_fmt yuv420p -preset medium -c:a aac -b:a 128k -movflags +faststart output.mp4", plan5.Commands[0].ToCommandLineString());
    }
}

