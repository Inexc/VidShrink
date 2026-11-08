using System;

namespace VidShrink.Core.Models;

public record Preset
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public CompressionMode Mode { get; init; } = CompressionMode.TargetSize;
    public double? TargetSizeMb { get; init; }
    public int? TargetBitrateKbps { get; init; }
    public int? Crf { get; init; }
    public VideoCodec VideoCodec { get; init; } = VideoCodec.H264;
    public HardwareAcceleration PreferredHardware { get; init; } = HardwareAcceleration.None;
    public int? MaxWidth { get; init; }
    public int? MaxHeight { get; init; }
    public int? FpsLimit { get; init; }
    public AudioMode AudioMode { get; init; } = AudioMode.Aac;
    public int AudioBitrateKbps { get; init; } = 128;
    public bool TwoPass { get; init; } = true;
    public SpeedPreset Speed { get; init; } = SpeedPreset.Medium;
}

public record CompressionSettings
{
    public required string InputFilePath { get; init; }
    public required string OutputFilePath { get; init; }
    public CompressionMode Mode { get; init; } = CompressionMode.TargetSize;
    public double? TargetSizeMb { get; init; }
    public int? TargetVideoBitrateKbps { get; init; }
    public int? Crf { get; init; }
    public VideoCodec VideoCodec { get; init; } = VideoCodec.H264;
    public HardwareAcceleration HardwareAcceleration { get; init; } = HardwareAcceleration.None;
    public string? EncoderName { get; init; }
    public int? OutputWidth { get; init; }
    public int? OutputHeight { get; init; }
    public int? FpsLimit { get; init; }
    public AudioMode AudioMode { get; init; } = AudioMode.Aac;
    public int AudioBitrateKbps { get; init; } = 128;
    public bool TwoPass { get; init; } = true;
    public SpeedPreset Speed { get; init; } = SpeedPreset.Medium;
    public string? VaapiDevice { get; init; }
    public TimeSpan Duration { get; init; }
    public TimeSpan? StartTime { get; init; }
    public TimeSpan? EndTime { get; init; }
}

public record BitrateCalculationResult
{
    public int VideoBitrateKbps { get; init; }
    public int AudioBitrateKbps { get; init; }
    public int TotalBitrateKbps { get; init; }
    public long EstimatedFileSizeBytes { get; init; }
    public bool IsClamped { get; init; }
    public string? Message { get; init; }
}

public record ProgressInfo
{
    public TimeSpan ProcessedTime { get; init; }
    public TimeSpan TotalDuration { get; init; }
    public double Percent { get; init; }
    public double PassPercent { get; init; }
    public double Fps { get; init; }
    public double SpeedRatio { get; init; }
    public TimeSpan? Eta { get; init; }
    public int CurrentPass { get; init; } = 1;
    public int TotalPasses { get; init; } = 1;
    public long ProcessedSizeBytes { get; init; }
}
