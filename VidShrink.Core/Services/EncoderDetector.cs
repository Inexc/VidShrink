using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VidShrink.Core.Models;
using VidShrink.Core.Platform;

namespace VidShrink.Core.Services;

public interface IEncoderDetector : IHardwareDetector
{
    new Task<IReadOnlyList<EncoderDescriptor>> DetectAvailableEncodersAsync(
        string? ffmpegPath = null,
        bool verifyWithTestEncode = true,
        CancellationToken cancellationToken = default);

    IReadOnlyList<EncoderDescriptor> ParseEncodersOutput(string output, PlatformKind platform);
}

public class EncoderDetector : IEncoderDetector
{
    private readonly IFfmpegLocator _locator;
    private readonly IPlatformService _platformService;
    private readonly IProcessRunner _processRunner;

    public EncoderDetector(
        IFfmpegLocator? locator = null,
        IPlatformService? platformService = null,
        IProcessRunner? processRunner = null)
    {
        _locator = locator ?? new FfmpegLocator();
        _platformService = platformService ?? DefaultPlatformService.Instance;
        _processRunner = processRunner ?? new DefaultProcessRunner();
    }

    public async Task<IReadOnlyList<EncoderDescriptor>> DetectAvailableEncodersAsync(
        string? ffmpegPath = null,
        bool verifyWithTestEncode = true,
        CancellationToken cancellationToken = default)
    {
        var ffmpegExe = ffmpegPath ?? _locator.FindExecutable("ffmpeg");
        if (string.IsNullOrEmpty(ffmpegExe))
        {
            // If ffmpeg executable is not found, return empty or safe fallback
            return Array.Empty<EncoderDescriptor>();
        }

        // 1. Query `ffmpeg -encoders`
        string encodersOutput = await RunFfmpegAsync(ffmpegExe, new[] { "-encoders" }, cancellationToken).ConfigureAwait(false);
        var candidates = ParseEncodersOutput(encodersOutput, _platformService.CurrentPlatform);

        if (!verifyWithTestEncode)
        {
            return candidates;
        }

        // 2. Verify hardware encoders with a 1-second test encode
        var verified = new List<EncoderDescriptor>();
        foreach (var desc in candidates)
        {
            if (!desc.IsHardware)
            {
                // Software encoders present in ffmpeg are considered available
                verified.Add(desc);
                continue;
            }

            bool works = await TestEncoderAsync(ffmpegExe, desc, cancellationToken).ConfigureAwait(false);
            if (works)
            {
                verified.Add(desc);
            }
        }

        return verified;
    }

    public IReadOnlyList<EncoderDescriptor> ParseEncodersOutput(string output, PlatformKind platform)
    {
        if (string.IsNullOrWhiteSpace(output))
            return Array.Empty<EncoderDescriptor>();

        // Regex matches video encoder lines like:
        //  V..... libx264              H.264 / AVC / MPEG-4 AVC...
        //  V..... h264_nvenc           NVIDIA NVENC H.264 encoder
        var regex = new Regex(@"^\s*V[A-Za-z.0-9]{5}\s+([a-zA-Z0-9_\-]+)\s+(.*)$", RegexOptions.Multiline);
        var matches = regex.Matches(output);

        var recognized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in matches)
        {
            recognized.Add(match.Groups[1].Value.Trim());
        }

        var results = new List<EncoderDescriptor>();

        // Candidate definitions
        var candidateList = GetKnownEncodersForPlatform(platform);

        foreach (var (name, displayName, codec, accel, desc) in candidateList)
        {
            if (recognized.Contains(name))
            {
                results.Add(new EncoderDescriptor
                {
                    Name = name,
                    DisplayName = displayName,
                    Codec = codec,
                    Acceleration = accel,
                    Description = desc
                });
            }
        }

        return results;
    }

    private static List<(string Name, string DisplayName, VideoCodec Codec, HardwareAcceleration Accel, string Desc)>
        GetKnownEncodersForPlatform(PlatformKind platform)
    {
        var list = new List<(string, string, VideoCodec, HardwareAcceleration, string)>
        {
            // Software (cross-platform)
            ("libx264", "H.264 (CPU / libx264)", VideoCodec.H264, HardwareAcceleration.None, "Standard CPU software encoder"),
            ("libx265", "HEVC (CPU / libx265)", VideoCodec.H265, HardwareAcceleration.None, "High efficiency CPU software encoder"),
            ("libsvtav1", "AV1 (CPU / SVT-AV1)", VideoCodec.AV1, HardwareAcceleration.None, "Next-gen AV1 CPU encoder"),
            ("libvpx-vp9", "VP9 (CPU / libvpx)", VideoCodec.VP9, HardwareAcceleration.None, "Google VP9 CPU encoder")
        };

        if (platform == PlatformKind.Windows)
        {
            // Windows: NVENC, QSV, AMF
            list.Add(("h264_nvenc", "H.264 (NVIDIA NVENC)", VideoCodec.H264, HardwareAcceleration.Nvenc, "NVIDIA hardware acceleration"));
            list.Add(("hevc_nvenc", "HEVC (NVIDIA NVENC)", VideoCodec.H265, HardwareAcceleration.Nvenc, "NVIDIA hardware acceleration"));
            list.Add(("av1_nvenc", "AV1 (NVIDIA NVENC)", VideoCodec.AV1, HardwareAcceleration.Nvenc, "NVIDIA hardware acceleration"));

            list.Add(("h264_qsv", "H.264 (Intel QuickSync)", VideoCodec.H264, HardwareAcceleration.Qsv, "Intel QuickSync hardware acceleration"));
            list.Add(("hevc_qsv", "HEVC (Intel QuickSync)", VideoCodec.H265, HardwareAcceleration.Qsv, "Intel QuickSync hardware acceleration"));
            list.Add(("av1_qsv", "AV1 (Intel QuickSync)", VideoCodec.AV1, HardwareAcceleration.Qsv, "Intel QuickSync hardware acceleration"));

            list.Add(("h264_amf", "H.264 (AMD AMF)", VideoCodec.H264, HardwareAcceleration.Amf, "AMD AMF hardware acceleration"));
            list.Add(("hevc_amf", "HEVC (AMD AMF)", VideoCodec.H265, HardwareAcceleration.Amf, "AMD AMF hardware acceleration"));
            list.Add(("av1_amf", "AV1 (AMD AMF)", VideoCodec.AV1, HardwareAcceleration.Amf, "AMD AMF hardware acceleration"));
        }
        else if (platform == PlatformKind.Linux)
        {
            // Linux: NVENC, VAAPI
            list.Add(("h264_nvenc", "H.264 (NVIDIA NVENC)", VideoCodec.H264, HardwareAcceleration.Nvenc, "NVIDIA hardware acceleration"));
            list.Add(("hevc_nvenc", "HEVC (NVIDIA NVENC)", VideoCodec.H265, HardwareAcceleration.Nvenc, "NVIDIA hardware acceleration"));
            list.Add(("av1_nvenc", "AV1 (NVIDIA NVENC)", VideoCodec.AV1, HardwareAcceleration.Nvenc, "NVIDIA hardware acceleration"));

            list.Add(("h264_vaapi", "H.264 (VAAPI)", VideoCodec.H264, HardwareAcceleration.Vaapi, "Linux VAAPI hardware acceleration"));
            list.Add(("hevc_vaapi", "HEVC (VAAPI)", VideoCodec.H265, HardwareAcceleration.Vaapi, "Linux VAAPI hardware acceleration"));
            list.Add(("av1_vaapi", "AV1 (VAAPI)", VideoCodec.AV1, HardwareAcceleration.Vaapi, "Linux VAAPI hardware acceleration"));
        }
        else
        {
            // Fallback: allow nvenc if present
            list.Add(("h264_nvenc", "H.264 (NVIDIA NVENC)", VideoCodec.H264, HardwareAcceleration.Nvenc, "NVIDIA hardware acceleration"));
            list.Add(("hevc_nvenc", "HEVC (NVIDIA NVENC)", VideoCodec.H265, HardwareAcceleration.Nvenc, "NVIDIA hardware acceleration"));
        }

        return list;
    }

    private async Task<bool> TestEncoderAsync(
        string ffmpegExe,
        EncoderDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        var args = new List<string> { "-y", "-hide_banner" };

        if (descriptor.Acceleration == HardwareAcceleration.Vaapi)
        {
            args.Add("-vaapi_device");
            args.Add("/dev/dri/renderD128");
        }

        args.Add("-f");
        args.Add("lavfi");
        args.Add("-i");
        args.Add("color=c=black:s=256x256:d=1");

        if (descriptor.Acceleration == HardwareAcceleration.Vaapi)
        {
            args.Add("-vf");
            args.Add("format=nv12,hwupload");
        }

        args.Add("-c:v");
        args.Add(descriptor.Name);

        args.Add("-f");
        args.Add("null");
        args.Add(_platformService.NullDevice);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            var result = await _processRunner.RunAsync(
                ffmpegExe,
                args,
                cancellationToken: cts.Token).ConfigureAwait(false);

            return result.ExitCode == 0 && !result.WasCancelled;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> RunFfmpegAsync(
        string ffmpegExe,
        IEnumerable<string> args,
        CancellationToken cancellationToken)
    {
        var stdoutSb = new StringBuilder();
        var result = await _processRunner.RunAsync(
            ffmpegExe,
            args.ToList(),
            onStandardOutputLine: line => stdoutSb.AppendLine(line),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return stdoutSb.ToString();
    }
}
