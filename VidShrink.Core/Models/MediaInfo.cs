using System;
using System.Collections.Generic;
using System.Linq;

namespace VidShrink.Core.Models;

public record VideoStreamInfo
{
    public int Index { get; init; }
    public string CodecName { get; init; } = string.Empty;
    public string? CodecLongName { get; init; }
    public string? Profile { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public double FrameRate { get; init; }
    public long Bitrate { get; init; }
    public int Rotation { get; init; }
    public string? PixelFormat { get; init; }
    public long TotalFrames { get; init; }
    public TimeSpan Duration { get; init; }

    public string ResolutionText => $"{Width}x{Height}";
}

public record AudioStreamInfo
{
    public int Index { get; init; }
    public string CodecName { get; init; } = string.Empty;
    public string? CodecLongName { get; init; }
    public int Channels { get; init; }
    public string? ChannelLayout { get; init; }
    public int SampleRate { get; init; }
    public long Bitrate { get; init; }
    public TimeSpan Duration { get; init; }
}

public record MediaInfo
{
    public string FilePath { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
    public TimeSpan Duration { get; init; }
    public long Bitrate { get; init; }
    public string FormatName { get; init; } = string.Empty;
    public string? FormatLongName { get; init; }

    public IReadOnlyList<VideoStreamInfo> VideoStreams { get; init; } = Array.Empty<VideoStreamInfo>();
    public IReadOnlyList<AudioStreamInfo> AudioStreams { get; init; } = Array.Empty<AudioStreamInfo>();

    public VideoStreamInfo? PrimaryVideoStream => VideoStreams.FirstOrDefault();
    public AudioStreamInfo? PrimaryAudioStream => AudioStreams.FirstOrDefault();

    public bool HasVideo => PrimaryVideoStream != null;
    public bool HasAudio => PrimaryAudioStream != null;

    public int Width => PrimaryVideoStream?.Width ?? 0;
    public int Height => PrimaryVideoStream?.Height ?? 0;
    public double FrameRate => PrimaryVideoStream?.FrameRate ?? 0.0;
    public string VideoCodec => PrimaryVideoStream?.CodecName ?? string.Empty;
    public string AudioCodec => PrimaryAudioStream?.CodecName ?? string.Empty;

    public double FileSizeMb => Math.Round(FileSizeBytes / (1024.0 * 1024.0), 2);
}
