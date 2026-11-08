using System;
using VidShrink.Core.Models;

namespace VidShrink.Core.Services;

public interface IBitrateCalculator
{
    BitrateCalculationResult Calculate(
        double targetSizeMb,
        TimeSpan duration,
        int audioBitrateKbps = 128,
        double overheadPercent = BitrateCalculator.DefaultOverheadPercent,
        int minVideoBitrateKbps = 32,
        bool autoAdjustAudioOnBudgetPressure = true);

    int CalculateVideoBitrate(
        double targetSizeMb,
        TimeSpan duration,
        int audioBitrateKbps = 128,
        int minVideoBitrateKbps = 32);
}

public class BitrateCalculator : IBitrateCalculator
{
    public const int DefaultMinVideoBitrateKbps = 32;
    public const int AbsoluteMinVideoBitrateKbps = 8;
    public const int AbsoluteMinAudioBitrateKbps = 32;
    public const double DefaultOverheadPercent = 3.0; // 3% container overhead: targetBytes * 8 * 0.97 / duration

    public BitrateCalculationResult Calculate(
        double targetSizeMb,
        TimeSpan duration,
        int audioBitrateKbps = 128,
        double overheadPercent = DefaultOverheadPercent,
        int minVideoBitrateKbps = DefaultMinVideoBitrateKbps,
        bool autoAdjustAudioOnBudgetPressure = true)
    {
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration), "Duration must be greater than zero.");

        if (targetSizeMb <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetSizeMb), "Target size must be greater than zero.");

        if (audioBitrateKbps < 0)
            throw new ArgumentOutOfRangeException(nameof(audioBitrateKbps), "Audio bitrate cannot be negative.");

        if (minVideoBitrateKbps < AbsoluteMinVideoBitrateKbps)
            minVideoBitrateKbps = AbsoluteMinVideoBitrateKbps;

        if (overheadPercent < 0 || overheadPercent >= 50)
            overheadPercent = DefaultOverheadPercent;

        double targetBytes = targetSizeMb * 1024.0 * 1024.0;
        double usableBytes = targetBytes * (1.0 - (overheadPercent / 100.0));
        double durationSeconds = duration.TotalSeconds;

        // Total available bitrate in kilobits per second (1 kbit = 1000 bits as per ffmpeg conventions)
        double totalBudgetKbps = (usableBytes * 8.0) / (durationSeconds * 1000.0);

        int effectiveAudioKbps = audioBitrateKbps;
        bool isClamped = false;
        string? message = null;

        // If audio alone consumes too much of total budget, optionally adjust audio down
        if (autoAdjustAudioOnBudgetPressure && effectiveAudioKbps > 0)
        {
            double maxAllowedAudioBudget = totalBudgetKbps * 0.40;
            if (totalBudgetKbps < (effectiveAudioKbps + minVideoBitrateKbps))
            {
                effectiveAudioKbps = Math.Max(AbsoluteMinAudioBitrateKbps, (int)Math.Floor(maxAllowedAudioBudget));
                isClamped = true;
                message = "слишком маленький лимит для этого видео, уменьши разрешение";
            }
        }

        double remainingForVideo = totalBudgetKbps - effectiveAudioKbps;
        int videoBitrateKbps = (int)Math.Floor(remainingForVideo);

        if (videoBitrateKbps < minVideoBitrateKbps)
        {
            videoBitrateKbps = minVideoBitrateKbps;
            isClamped = true;
            message = "слишком маленький лимит для этого видео, уменьши разрешение";
        }

        // Never allow negative or zero bitrate
        videoBitrateKbps = Math.Max(AbsoluteMinVideoBitrateKbps, videoBitrateKbps);

        int totalBitrateKbps = videoBitrateKbps + effectiveAudioKbps;

        // Estimated final file size in bytes
        double totalDataBitsPerSecond = totalBitrateKbps * 1000.0;
        double rawBytes = (totalDataBitsPerSecond / 8.0) * durationSeconds;
        long estimatedFileSizeBytes = (long)Math.Round(rawBytes / (1.0 - (overheadPercent / 100.0)));

        return new BitrateCalculationResult
        {
            VideoBitrateKbps = videoBitrateKbps,
            AudioBitrateKbps = effectiveAudioKbps,
            TotalBitrateKbps = totalBitrateKbps,
            EstimatedFileSizeBytes = estimatedFileSizeBytes,
            IsClamped = isClamped,
            Message = message
        };
    }

    public int CalculateVideoBitrate(
        double targetSizeMb,
        TimeSpan duration,
        int audioBitrateKbps = 128,
        int minVideoBitrateKbps = DefaultMinVideoBitrateKbps)
    {
        var result = Calculate(targetSizeMb, duration, audioBitrateKbps, minVideoBitrateKbps: minVideoBitrateKbps);
        return result.VideoBitrateKbps;
    }
}
