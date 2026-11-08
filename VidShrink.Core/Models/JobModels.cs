using System;
using System.IO;

namespace VidShrink.Core.Models;

public enum JobStatus
{
    Queued,
    ProcessingPass1,
    ProcessingPass2,
    Processing,
    Completed,
    Failed,
    Cancelled
}

public record EncodeResult
{
    public required bool Success { get; init; }
    public required string OutputFilePath { get; init; }
    public long OutputSizeBytes { get; init; }
    public TimeSpan ElapsedTime { get; init; }
    public bool RetriedWithLowerBitrate { get; init; }
    public string? ErrorMessage { get; init; }
}

public class CompressionJob
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required string InputFilePath { get; init; }
    public required string OutputFilePath { get; set; }
    public required CompressionSettings Settings { get; set; }
    public MediaInfo? SourceInfo { get; init; }
    public JobStatus Status { get; set; } = JobStatus.Queued;
    public string? StatusMessage { get; set; }
    public ProgressInfo? CurrentProgress { get; set; }
    public EncodeResult? Result { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public long OriginalSizeBytes { get; set; }

    public long? CompressedSizeBytes => Result?.OutputSizeBytes;

    public long? SavedBytes => CompressedSizeBytes.HasValue && OriginalSizeBytes > 0
        ? OriginalSizeBytes - CompressedSizeBytes.Value
        : null;

    public double? SavingsPercentage => CompressedSizeBytes.HasValue && OriginalSizeBytes > 0
        ? Math.Round(((OriginalSizeBytes - (double)CompressedSizeBytes.Value) / OriginalSizeBytes) * 100.0, 1)
        : null;

    public string? Error { get; set; }
}
