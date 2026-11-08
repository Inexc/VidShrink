using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using VidShrink.Core.Models;
using VidShrink.Core.Platform;

namespace VidShrink.Core.Services;

public record FfmpegCommand
{
    public required IReadOnlyList<string> Arguments { get; init; }
    public required string OutputPath { get; init; }
    public int PassIndex { get; init; } = 1;
    public int TotalPasses { get; init; } = 1;
    public bool IsPass1 => PassIndex == 1 && TotalPasses == 2;
    public string Description { get; init; } = string.Empty;

    public string ToCommandLineString(string executable = "ffmpeg")
    {
        var sb = new StringBuilder();
        sb.Append(executable);
        foreach (var arg in Arguments)
        {
            sb.Append(' ');
            if (arg.Contains(' ') || arg.Contains('"') || string.IsNullOrEmpty(arg))
            {
                sb.Append('"').Append(arg.Replace("\"", "\\\"")).Append('"');
            }
            else
            {
                sb.Append(arg);
            }
        }
        return sb.ToString();
    }
}

public record CommandPlan
{
    public required IReadOnlyList<FfmpegCommand> Commands { get; init; }
    public string? PassLogPrefix { get; init; }
    public required CompressionSettings Settings { get; init; }
}

public interface ICommandBuilder
{
    CommandPlan BuildPlan(CompressionSettings settings, string? passLogPrefix = null);
    string ResolveEncoder(VideoCodec codec, HardwareAcceleration acceleration, string? customEncoder = null);
}

public class CommandBuilder : ICommandBuilder
{
    private readonly IPlatformService _platformService;
    private readonly IBitrateCalculator _bitrateCalculator;

    public CommandBuilder(
        IPlatformService? platformService = null,
        IBitrateCalculator? bitrateCalculator = null)
    {
        _platformService = platformService ?? DefaultPlatformService.Instance;
        _bitrateCalculator = bitrateCalculator ?? new BitrateCalculator();
    }

    public string ResolveEncoder(VideoCodec codec, HardwareAcceleration acceleration, string? customEncoder = null)
    {
        if (!string.IsNullOrWhiteSpace(customEncoder))
            return customEncoder;

        return (codec, acceleration) switch
        {
            (VideoCodec.H264, HardwareAcceleration.None) => "libx264",
            (VideoCodec.H264, HardwareAcceleration.Nvenc) => "h264_nvenc",
            (VideoCodec.H264, HardwareAcceleration.Qsv) => "h264_qsv",
            (VideoCodec.H264, HardwareAcceleration.Amf) => "h264_amf",
            (VideoCodec.H264, HardwareAcceleration.Vaapi) => "h264_vaapi",

            (VideoCodec.H265, HardwareAcceleration.None) => "libx265",
            (VideoCodec.H265, HardwareAcceleration.Nvenc) => "hevc_nvenc",
            (VideoCodec.H265, HardwareAcceleration.Qsv) => "hevc_qsv",
            (VideoCodec.H265, HardwareAcceleration.Amf) => "hevc_amf",
            (VideoCodec.H265, HardwareAcceleration.Vaapi) => "hevc_vaapi",

            (VideoCodec.AV1, HardwareAcceleration.None) => "libsvtav1",
            (VideoCodec.AV1, HardwareAcceleration.Nvenc) => "av1_nvenc",
            (VideoCodec.AV1, HardwareAcceleration.Qsv) => "av1_qsv",
            (VideoCodec.AV1, HardwareAcceleration.Amf) => "av1_amf",
            (VideoCodec.AV1, HardwareAcceleration.Vaapi) => "av1_vaapi",

            (VideoCodec.VP9, _) => "libvpx-vp9",

            _ => "libx264"
        };
    }

    public CommandPlan BuildPlan(CompressionSettings settings, string? passLogPrefix = null)
    {
        if (string.IsNullOrWhiteSpace(settings.InputFilePath))
            throw new ArgumentException("Input file path must not be empty.", nameof(settings));

        if (string.IsNullOrWhiteSpace(settings.OutputFilePath))
            throw new ArgumentException("Output file path must not be empty.", nameof(settings));

        // Resolve video bitrate if Mode is TargetSize
        int targetVideoBitrateKbps = settings.TargetVideoBitrateKbps ?? 0;
        int effectiveAudioBitrateKbps = settings.AudioMode == AudioMode.Mute ? 0 : settings.AudioBitrateKbps;

        if (targetVideoBitrateKbps <= 0 && settings.Mode == CompressionMode.TargetSize && settings.TargetSizeMb.HasValue && settings.Duration > TimeSpan.Zero)
        {
            var calcResult = _bitrateCalculator.Calculate(
                settings.TargetSizeMb.Value,
                settings.Duration,
                effectiveAudioBitrateKbps);

            targetVideoBitrateKbps = calcResult.VideoBitrateKbps;
            effectiveAudioBitrateKbps = calcResult.AudioBitrateKbps;
        }

        string encoder = ResolveEncoder(settings.VideoCodec, settings.HardwareAcceleration, settings.EncoderName);
        bool isVaapi = settings.HardwareAcceleration == HardwareAcceleration.Vaapi ||
                       encoder.EndsWith("_vaapi", StringComparison.OrdinalIgnoreCase);

        // Two-pass only for libx264 and libx265 in TargetSize mode. SVT-AV1 and hardware encoders use single pass.
        bool isTwoPass = settings.TwoPass &&
                         settings.Mode == CompressionMode.TargetSize &&
                         CanUseTwoPass(encoder);

        if (isTwoPass)
        {
            passLogPrefix ??= Path.Combine(Path.GetTempPath(), $"vidshrink2pass_{Guid.NewGuid():N}");

            var pass1Args = BuildArguments(
                settings,
                encoder,
                targetVideoBitrateKbps,
                effectiveAudioBitrateKbps,
                passIndex: 1,
                totalPasses: 2,
                passLogPrefix: passLogPrefix,
                isVaapi: isVaapi);

            var pass2Args = BuildArguments(
                settings,
                encoder,
                targetVideoBitrateKbps,
                effectiveAudioBitrateKbps,
                passIndex: 2,
                totalPasses: 2,
                passLogPrefix: passLogPrefix,
                isVaapi: isVaapi);

            var commands = new List<FfmpegCommand>
            {
                new FfmpegCommand
                {
                    Arguments = pass1Args,
                    OutputPath = _platformService.NullDevice,
                    PassIndex = 1,
                    TotalPasses = 2,
                    Description = "Pass 1 of 2 (Analysis)"
                },
                new FfmpegCommand
                {
                    Arguments = pass2Args,
                    OutputPath = settings.OutputFilePath,
                    PassIndex = 2,
                    TotalPasses = 2,
                    Description = "Pass 2 of 2 (Encoding)"
                }
            };

            return new CommandPlan
            {
                Commands = commands,
                PassLogPrefix = passLogPrefix,
                Settings = settings
            };
        }
        else
        {
            var singlePassArgs = BuildArguments(
                settings,
                encoder,
                targetVideoBitrateKbps,
                effectiveAudioBitrateKbps,
                passIndex: 1,
                totalPasses: 1,
                passLogPrefix: null,
                isVaapi: isVaapi);

            var commands = new List<FfmpegCommand>
            {
                new FfmpegCommand
                {
                    Arguments = singlePassArgs,
                    OutputPath = settings.OutputFilePath,
                    PassIndex = 1,
                    TotalPasses = 1,
                    Description = "Single Pass Encoding"
                }
            };

            return new CommandPlan
            {
                Commands = commands,
                PassLogPrefix = null,
                Settings = settings
            };
        }
    }

    private List<string> BuildArguments(
        CompressionSettings settings,
        string encoder,
        int videoBitrateKbps,
        int audioBitrateKbps,
        int passIndex,
        int totalPasses,
        string? passLogPrefix,
        bool isVaapi)
    {
        var args = new List<string>();

        // Overwrite output files without asking
        args.Add("-y");
        args.Add("-hide_banner");

        // Linux VAAPI hardware device initialization must occur before input
        if (isVaapi)
        {
            var vaapiDevice = !string.IsNullOrWhiteSpace(settings.VaapiDevice)
                ? settings.VaapiDevice
                : "/dev/dri/renderD128";

            args.Add("-vaapi_device");
            args.Add(vaapiDevice);
        }

        // Time trimming (-ss / -to before input for fast seeking)
        if (settings.StartTime.HasValue && settings.StartTime.Value > TimeSpan.Zero)
        {
            args.Add("-ss");
            args.Add(FormatTimeSpan(settings.StartTime.Value));
        }

        if (settings.EndTime.HasValue && settings.EndTime.Value > TimeSpan.Zero)
        {
            args.Add("-to");
            args.Add(FormatTimeSpan(settings.EndTime.Value));
        }

        // Input file
        args.Add("-i");
        args.Add(settings.InputFilePath);

        // Video filter chain
        var filter = BuildVideoFilter(settings, isVaapi);
        if (!string.IsNullOrEmpty(filter))
        {
            args.Add("-vf");
            args.Add(filter);
        }

        // Video codec
        args.Add("-c:v");
        args.Add(encoder);

        // Rate control & quality
        if (settings.Mode == CompressionMode.Quality)
        {
            ApplyQualitySettings(args, settings, encoder);
        }
        else
        {
            ApplyBitrateSettings(args, settings, encoder, videoBitrateKbps, passIndex, totalPasses, passLogPrefix);
        }

        // Pixel format (if not VAAPI which manages pixel format in filter/hardware)
        if (!isVaapi)
        {
            if (encoder == "libsvtav1")
            {
                args.Add("-pix_fmt");
                args.Add("yuv420p10le");
            }
            else if (encoder.StartsWith("libx264") || encoder.StartsWith("h264_nvenc") ||
                     encoder.StartsWith("libx265") || encoder.StartsWith("hevc_nvenc") ||
                     encoder.StartsWith("libvpx-vp9"))
            {
                args.Add("-pix_fmt");
                args.Add("yuv420p");
            }
        }

        // Speed / Preset
        ApplySpeedPreset(args, settings.Speed, encoder);

        // Framerate limit
        if (settings.FpsLimit.HasValue && settings.FpsLimit.Value > 0)
        {
            args.Add("-r");
            args.Add(settings.FpsLimit.Value.ToString());
        }

        // Pass 1 specific flags: no audio, null format
        if (passIndex == 1 && totalPasses == 2)
        {
            args.Add("-an");
            args.Add("-f");
            args.Add("null");
            args.Add(_platformService.NullDevice);
        }
        else
        {
            // Audio options
            ApplyAudioSettings(args, settings.AudioMode, audioBitrateKbps);

            // Container flags (faststart for mp4/mov and hvc1 tag for H.265)
            var ext = Path.GetExtension(settings.OutputFilePath).ToLowerInvariant();
            if (ext is ".mp4" or ".m4v" or ".mov")
            {
                args.Add("-movflags");
                args.Add("+faststart");

                if (settings.VideoCodec == VideoCodec.H265 ||
                    encoder.Contains("265", StringComparison.OrdinalIgnoreCase) ||
                    encoder.Contains("hevc", StringComparison.OrdinalIgnoreCase))
                {
                    args.Add("-tag:v");
                    args.Add("hvc1");
                }
            }

            // Output path
            args.Add(settings.OutputFilePath);
        }

        return args;
    }

    private static string? BuildVideoFilter(CompressionSettings settings, bool isVaapi)
    {
        var filterParts = new List<string>();

        // Scaling filter
        if (settings.OutputWidth.HasValue || settings.OutputHeight.HasValue)
        {
            string scaleStr;
            if (settings.OutputWidth.HasValue && settings.OutputHeight.HasValue)
            {
                scaleStr = $"scale='min({settings.OutputWidth.Value},iw)':'min({settings.OutputHeight.Value},ih)':force_original_aspect_ratio=decrease,pad=ceil(iw/2)*2:ceil(ih/2)*2";
            }
            else if (settings.OutputWidth.HasValue)
            {
                scaleStr = $"scale={settings.OutputWidth.Value}:-2";
            }
            else if (settings.OutputHeight.HasValue)
            {
                scaleStr = $"scale=-2:{settings.OutputHeight.Value}";
            }
            else
            {
                scaleStr = string.Empty;
            }

            filterParts.Add(scaleStr);
        }

        // VAAPI hwupload filter requirement
        if (isVaapi)
        {
            filterParts.Add("format=nv12");
            filterParts.Add("hwupload");
        }

        return filterParts.Count > 0 ? string.Join(",", filterParts) : null;
    }

    private static void ApplyQualitySettings(List<string> args, CompressionSettings settings, string encoder)
    {
        int crf = settings.Crf ?? 23;

        if (encoder.EndsWith("_nvenc", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("-cq");
            args.Add(crf.ToString());
        }
        else if (encoder.EndsWith("_vaapi", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("-qp");
            args.Add(crf.ToString());
        }
        else if (encoder.EndsWith("_qsv", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("-global_quality");
            args.Add(crf.ToString());
        }
        else if (encoder.EndsWith("_amf", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("-rc");
            args.Add("cqp");
            args.Add("-qp_i");
            args.Add(crf.ToString());
            args.Add("-qp_p");
            args.Add(crf.ToString());
        }
        else if (encoder == "libvpx-vp9")
        {
            args.Add("-crf");
            args.Add(crf.ToString());
            args.Add("-b:v");
            args.Add("0");
        }
        else
        {
            // libx264, libx265, libsvtav1
            args.Add("-crf");
            args.Add(crf.ToString());
        }
    }

    private static void ApplyBitrateSettings(
        List<string> args,
        CompressionSettings settings,
        string encoder,
        int videoBitrateKbps,
        int passIndex,
        int totalPasses,
        string? passLogPrefix)
    {
        if (videoBitrateKbps <= 0)
            videoBitrateKbps = 1000;

        args.Add("-b:v");
        args.Add($"{videoBitrateKbps}k");

        // Rate control buffer
        int maxRate = (int)(videoBitrateKbps * 1.5);
        int bufSize = videoBitrateKbps * 2;

        args.Add("-maxrate");
        args.Add($"{maxRate}k");
        args.Add("-bufsize");
        args.Add($"{bufSize}k");

        // Two-pass flags: x264 uses -pass/-passlogfile, x265 uses -x265-params pass=N:stats=...
        if (totalPasses == 2)
        {
            if (encoder.Equals("libx265", StringComparison.OrdinalIgnoreCase))
            {
                var statsFile = string.IsNullOrEmpty(passLogPrefix) ? "x265_2pass.log" : passLogPrefix;
                args.Add("-x265-params");
                args.Add($"pass={passIndex}:stats={statsFile}");
            }
            else
            {
                args.Add("-pass");
                args.Add(passIndex.ToString());

                if (!string.IsNullOrEmpty(passLogPrefix))
                {
                    args.Add("-passlogfile");
                    args.Add(passLogPrefix);
                }
            }
        }
    }

    private static void ApplySpeedPreset(List<string> args, SpeedPreset speed, string encoder)
    {
        if (encoder.EndsWith("_nvenc", StringComparison.OrdinalIgnoreCase))
        {
            string nvencPreset = speed switch
            {
                SpeedPreset.UltraFast or SpeedPreset.SuperFast => "p1",
                SpeedPreset.VeryFast => "p2",
                SpeedPreset.Faster => "p3",
                SpeedPreset.Fast => "p4",
                SpeedPreset.Medium => "p5",
                SpeedPreset.Slow => "p6",
                SpeedPreset.Slower or SpeedPreset.VerySlow => "p7",
                _ => "p4"
            };
            args.Add("-preset");
            args.Add(nvencPreset);
        }
        else if (encoder.EndsWith("_vaapi", StringComparison.OrdinalIgnoreCase))
        {
            // VAAPI doesn't use -preset standard string
        }
        else if (encoder.EndsWith("_qsv", StringComparison.OrdinalIgnoreCase))
        {
            string qsvPreset = speed switch
            {
                SpeedPreset.UltraFast or SpeedPreset.SuperFast or SpeedPreset.VeryFast => "veryfast",
                SpeedPreset.Faster or SpeedPreset.Fast => "faster",
                SpeedPreset.Medium => "medium",
                SpeedPreset.Slow => "slow",
                SpeedPreset.Slower or SpeedPreset.VerySlow => "veryslow",
                _ => "medium"
            };
            args.Add("-preset");
            args.Add(qsvPreset);
        }
        else if (encoder.EndsWith("_amf", StringComparison.OrdinalIgnoreCase))
        {
            string amfQuality = speed switch
            {
                SpeedPreset.UltraFast or SpeedPreset.SuperFast or SpeedPreset.VeryFast => "speed",
                SpeedPreset.Faster or SpeedPreset.Fast => "balanced",
                SpeedPreset.Medium => "balanced",
                SpeedPreset.Slow or SpeedPreset.Slower or SpeedPreset.VerySlow => "quality",
                _ => "balanced"
            };
            args.Add("-quality");
            args.Add(amfQuality);
        }
        else
        {
            // Software encoders libx264, libx265, libsvtav1
            string preset = speed switch
            {
                SpeedPreset.UltraFast => "ultrafast",
                SpeedPreset.SuperFast => "superfast",
                SpeedPreset.VeryFast => "veryfast",
                SpeedPreset.Faster => "faster",
                SpeedPreset.Fast => "fast",
                SpeedPreset.Medium => "medium",
                SpeedPreset.Slow => "slow",
                SpeedPreset.Slower => "slower",
                SpeedPreset.VerySlow => "veryslow",
                _ => "medium"
            };
            args.Add("-preset");
            args.Add(preset);
        }
    }

    private static void ApplyAudioSettings(List<string> args, AudioMode audioMode, int audioBitrateKbps)
    {
        switch (audioMode)
        {
            case AudioMode.Mute:
                args.Add("-an");
                break;
            case AudioMode.Copy:
                args.Add("-c:a");
                args.Add("copy");
                break;
            case AudioMode.Opus:
                args.Add("-c:a");
                args.Add("libopus");
                args.Add("-b:a");
                args.Add($"{audioBitrateKbps}k");
                break;
            case AudioMode.Aac:
            default:
                args.Add("-c:a");
                args.Add("aac");
                args.Add("-b:a");
                args.Add($"{audioBitrateKbps}k");
                break;
        }
    }

    private static bool CanUseTwoPass(string encoder)
    {
        return string.Equals(encoder, "libx264", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(encoder, "libx265", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatTimeSpan(TimeSpan time)
    {
        return time.ToString(@"hh\:mm\:ss\.fff", System.Globalization.CultureInfo.InvariantCulture);
    }
}
