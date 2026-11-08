using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VidShrink.Core.Models;

namespace VidShrink.Core.Services;

public interface IProbeService
{
    MediaInfo ParseJson(string json, string? filePath = null);
    Task<MediaInfo> ProbeAsync(string filePath, string? ffprobePath = null, CancellationToken cancellationToken = default);
}

public class ProbeService : IProbeService
{
    private readonly IFfmpegLocator _locator;

    public ProbeService(IFfmpegLocator? locator = null)
    {
        _locator = locator ?? new FfmpegLocator();
    }

    public async Task<MediaInfo> ProbeAsync(
        string filePath,
        string? ffprobePath = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path must not be empty.", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException("Input media file not found.", filePath);

        var probeExe = ffprobePath ?? _locator.FindExecutable("ffprobe");
        if (string.IsNullOrEmpty(probeExe) || !File.Exists(probeExe))
        {
            throw new InvalidOperationException(
                "ffprobe executable was not found. Please ensure ffmpeg/ffprobe is installed or placed in the application directory.");
        }

        var psi = new ProcessStartInfo
        {
            FileName = probeExe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("-v");
        psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-print_format");
        psi.ArgumentList.Add("json");
        psi.ArgumentList.Add("-show_format");
        psi.ArgumentList.Add("-show_streams");
        psi.ArgumentList.Add("-show_error");
        psi.ArgumentList.Add(filePath);

        using var process = new Process { StartInfo = psi };

        try
        {
            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"ffprobe failed with exit code {process.ExitCode}: {stderr.Trim()}");
            }

            if (string.IsNullOrWhiteSpace(stdout))
            {
                throw new InvalidOperationException("ffprobe returned empty output.");
            }

            return ParseJson(stdout, filePath);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Best-effort process cleanup
            }
            throw;
        }
    }

    public MediaInfo ParseJson(string json, string? filePath = null)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("JSON content cannot be empty.", nameof(json));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Check for error property in ffprobe output
        if (root.TryGetProperty("error", out var errorEl))
        {
            string errStr = errorEl.TryGetProperty("string", out var s) ? s.GetString() ?? "Unknown probe error" : "Unknown probe error";
            throw new InvalidOperationException($"ffprobe reported error: {errStr}");
        }

        long fileSizeBytes = 0;
        TimeSpan formatDuration = TimeSpan.Zero;
        long formatBitrate = 0;
        string formatName = string.Empty;
        string? formatLongName = null;

        if (root.TryGetProperty("format", out var formatEl))
        {
            if (string.IsNullOrEmpty(filePath) && formatEl.TryGetProperty("filename", out var fnEl))
            {
                filePath = fnEl.GetString();
            }

            if (formatEl.TryGetProperty("size", out var sizeEl))
            {
                fileSizeBytes = ParseLong(sizeEl);
            }

            if (formatEl.TryGetProperty("duration", out var durEl))
            {
                formatDuration = ParseDuration(durEl);
            }

            if (formatEl.TryGetProperty("bit_rate", out var brEl))
            {
                formatBitrate = ParseLong(brEl);
            }

            if (formatEl.TryGetProperty("format_name", out var fnNameEl))
            {
                formatName = fnNameEl.GetString() ?? string.Empty;
            }

            if (formatEl.TryGetProperty("format_long_name", out var flnEl))
            {
                formatLongName = flnEl.GetString();
            }
        }

        // If file exists on disk and size wasn't parsed, use disk size
        if (fileSizeBytes == 0 && !string.IsNullOrEmpty(filePath) && File.Exists(filePath))
        {
            try
            {
                fileSizeBytes = new FileInfo(filePath).Length;
            }
            catch
            {
                // Ignore filesystem read errors
            }
        }

        var videoStreams = new List<VideoStreamInfo>();
        var audioStreams = new List<AudioStreamInfo>();

        if (root.TryGetProperty("streams", out var streamsEl) && streamsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var streamEl in streamsEl.EnumerateArray())
            {
                string codecType = streamEl.TryGetProperty("codec_type", out var ct) ? ct.GetString() ?? "" : "";
                int index = streamEl.TryGetProperty("index", out var idx) ? idx.GetInt32() : 0;
                string codecName = streamEl.TryGetProperty("codec_name", out var cn) ? cn.GetString() ?? "" : "";
                string? codecLongName = streamEl.TryGetProperty("codec_long_name", out var cln) ? cln.GetString() : null;
                string? profile = streamEl.TryGetProperty("profile", out var prof) ? prof.GetString() : null;

                long streamBitrate = streamEl.TryGetProperty("bit_rate", out var sbr) ? ParseLong(sbr) : 0;
                TimeSpan streamDuration = streamEl.TryGetProperty("duration", out var sdur) ? ParseDuration(sdur) : formatDuration;

                if (codecType.Equals("video", StringComparison.OrdinalIgnoreCase))
                {
                    int width = streamEl.TryGetProperty("width", out var w) ? w.GetInt32() : 0;
                    int height = streamEl.TryGetProperty("height", out var h) ? h.GetInt32() : 0;

                    double frameRate = 0.0;
                    if (streamEl.TryGetProperty("r_frame_rate", out var rfr))
                    {
                        frameRate = ParseFrameRate(rfr.GetString());
                    }
                    if (frameRate <= 0.0 && streamEl.TryGetProperty("avg_frame_rate", out var afr))
                    {
                        frameRate = ParseFrameRate(afr.GetString());
                    }

                    int rotation = ExtractRotation(streamEl);
                    string? pixFmt = streamEl.TryGetProperty("pix_fmt", out var pf) ? pf.GetString() : null;
                    long totalFrames = streamEl.TryGetProperty("nb_frames", out var nbf) ? ParseLong(nbf) : 0;

                    videoStreams.Add(new VideoStreamInfo
                    {
                        Index = index,
                        CodecName = codecName,
                        CodecLongName = codecLongName,
                        Profile = profile,
                        Width = width,
                        Height = height,
                        FrameRate = frameRate,
                        Bitrate = streamBitrate,
                        Rotation = rotation,
                        PixelFormat = pixFmt,
                        TotalFrames = totalFrames,
                        Duration = streamDuration != TimeSpan.Zero ? streamDuration : formatDuration
                    });
                }
                else if (codecType.Equals("audio", StringComparison.OrdinalIgnoreCase))
                {
                    int channels = streamEl.TryGetProperty("channels", out var ch) ? ch.GetInt32() : 0;
                    string? channelLayout = streamEl.TryGetProperty("channel_layout", out var chl) ? chl.GetString() : null;
                    int sampleRate = streamEl.TryGetProperty("sample_rate", out var sr) ? (int)ParseLong(sr) : 0;

                    audioStreams.Add(new AudioStreamInfo
                    {
                        Index = index,
                        CodecName = codecName,
                        CodecLongName = codecLongName,
                        Channels = channels,
                        ChannelLayout = channelLayout,
                        SampleRate = sampleRate,
                        Bitrate = streamBitrate,
                        Duration = streamDuration != TimeSpan.Zero ? streamDuration : formatDuration
                    });
                }
            }
        }

        // Final duration resolution
        var primaryDuration = formatDuration;
        if (primaryDuration == TimeSpan.Zero && videoStreams.Count > 0)
        {
            primaryDuration = videoStreams[0].Duration;
        }
        if (primaryDuration == TimeSpan.Zero && audioStreams.Count > 0)
        {
            primaryDuration = audioStreams[0].Duration;
        }

        return new MediaInfo
        {
            FilePath = filePath ?? string.Empty,
            FileSizeBytes = fileSizeBytes,
            Duration = primaryDuration,
            Bitrate = formatBitrate,
            FormatName = formatName,
            FormatLongName = formatLongName,
            VideoStreams = videoStreams,
            AudioStreams = audioStreams
        };
    }

    private static int ExtractRotation(JsonElement streamEl)
    {
        // 1. Check tags.rotate
        if (streamEl.TryGetProperty("tags", out var tagsEl))
        {
            if (tagsEl.TryGetProperty("rotate", out var rotEl))
            {
                var rotStr = rotEl.GetString();
                if (double.TryParse(rotStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var rotVal))
                {
                    return NormalizeRotation((int)Math.Round(rotVal));
                }
            }
        }

        // 2. Check side_data_list for rotation
        if (streamEl.TryGetProperty("side_data_list", out var sideDataEl) && sideDataEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in sideDataEl.EnumerateArray())
            {
                if (item.TryGetProperty("rotation", out var rEl))
                {
                    if (rEl.ValueKind == JsonValueKind.Number && rEl.TryGetDouble(out var rNum))
                    {
                        return NormalizeRotation((int)Math.Round(rNum));
                    }
                    if (rEl.ValueKind == JsonValueKind.String &&
                        double.TryParse(rEl.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var rParsed))
                    {
                        return NormalizeRotation((int)Math.Round(rParsed));
                    }
                }
            }
        }

        return 0;
    }

    private static int NormalizeRotation(int rotation)
    {
        var normalized = rotation % 360;
        if (normalized < 0) normalized += 360;
        return normalized;
    }

    private static double ParseFrameRate(string? fraction)
    {
        if (string.IsNullOrWhiteSpace(fraction)) return 0.0;

        var parts = fraction.Split('/');
        if (parts.Length == 2)
        {
            if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var num) &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var den) &&
                den != 0.0)
            {
                return Math.Round(num / den, 3);
            }
        }
        else if (parts.Length == 1)
        {
            if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var singleVal))
            {
                return Math.Round(singleVal, 3);
            }
        }

        return 0.0;
    }

    private static TimeSpan ParseDuration(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            if (element.TryGetDouble(out var d))
                return TimeSpan.FromSeconds(d);
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            var str = element.GetString();
            if (double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
                return TimeSpan.FromSeconds(s);
        }

        return TimeSpan.Zero;
    }

    private static long ParseLong(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            if (element.TryGetInt64(out var val))
                return val;
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            var str = element.GetString();
            if (long.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
        }

        return 0;
    }
}
