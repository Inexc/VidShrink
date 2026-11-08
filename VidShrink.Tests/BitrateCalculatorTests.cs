using System;
using VidShrink.Core.Services;
using Xunit;

namespace VidShrink.Tests;

public class BitrateCalculatorTests
{
    private readonly BitrateCalculator _calculator = new();

    [Fact]
    public void Calculate_Discord8MbStandardVideo_CalculatesReasonableBitrates()
    {
        // 8 MB file for a 60-second video with 96 kbps audio
        var duration = TimeSpan.FromSeconds(60);
        var result = _calculator.Calculate(targetSizeMb: 8.0, duration: duration, audioBitrateKbps: 96);

        // Usable bytes ~ 8 * 1024 * 1024 * 0.985 = 8,262,778 bytes -> 66,102,231 bits
        // Total kbps ~ 66,102,231 / 60,000 ~ 1101 kbps
        // Video kbps ~ 1101 - 96 ~ 1005 kbps
        Assert.InRange(result.VideoBitrateKbps, 950, 1100);
        Assert.Equal(96, result.AudioBitrateKbps);
        Assert.False(result.IsClamped);
        Assert.True(result.EstimatedFileSizeBytes <= 8 * 1024 * 1024);
    }

    [Fact]
    public void Calculate_Discord25MbTwoMinuteVideo_CalculatesExpectedBitrates()
    {
        var duration = TimeSpan.FromMinutes(2); // 120s
        var result = _calculator.Calculate(targetSizeMb: 25.0, duration: duration, audioBitrateKbps: 128);

        // Total available ~ 25 * 1024 * 1024 * 0.985 * 8 / (120 * 1000) ~ 1721 kbps
        // Video ~ 1721 - 128 = 1593 kbps
        Assert.InRange(result.VideoBitrateKbps, 1500, 1650);
        Assert.Equal(128, result.AudioBitrateKbps);
        Assert.False(result.IsClamped);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Calculate_ZeroOrNegativeDuration_ThrowsArgumentOutOfRangeException(double seconds)
    {
        var duration = TimeSpan.FromSeconds(seconds);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _calculator.Calculate(targetSizeMb: 8.0, duration: duration));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    public void Calculate_ZeroOrNegativeTargetSize_ThrowsArgumentOutOfRangeException(double sizeMb)
    {
        var duration = TimeSpan.FromSeconds(60);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _calculator.Calculate(targetSizeMb: sizeMb, duration: duration));
    }

    [Fact]
    public void Calculate_NegativeAudioBitrate_ThrowsArgumentOutOfRangeException()
    {
        var duration = TimeSpan.FromSeconds(60);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _calculator.Calculate(targetSizeMb: 8.0, duration: duration, audioBitrateKbps: -10));
    }

    [Fact]
    public void Calculate_ExtremelySmallLimit_ClampsToMinimumAndNeverNegative()
    {
        // 0.05 MB target for 10-minute video -> available budget is minuscule (~0.68 kbps!)
        var duration = TimeSpan.FromMinutes(10);
        var result = _calculator.Calculate(targetSizeMb: 0.05, duration: duration, audioBitrateKbps: 128);

        Assert.True(result.VideoBitrateKbps >= BitrateCalculator.AbsoluteMinVideoBitrateKbps,
            $"Video bitrate was {result.VideoBitrateKbps}, expected >= {BitrateCalculator.AbsoluteMinVideoBitrateKbps}");
        Assert.True(result.IsClamped);
        Assert.NotNull(result.Message);
        Assert.Contains("слишком маленький лимит для этого видео, уменьши разрешение", result.Message);
        Assert.True(result.TotalBitrateKbps > 0);
    }

    [Fact]
    public void Calculate_AudioConsumesMoreThanTotalBudget_GuaranteesPositiveVideoBitrate()
    {
        // 0.5 MB target for 2-minute video -> total budget ~ 34 kbps, audio asked for 128 kbps
        var duration = TimeSpan.FromMinutes(2);
        var result = _calculator.Calculate(targetSizeMb: 0.5, duration: duration, audioBitrateKbps: 128);

        Assert.True(result.VideoBitrateKbps > 0, "Video bitrate must never be negative or zero");
        Assert.True(result.AudioBitrateKbps > 0, "Audio bitrate must never be negative or zero");
        Assert.True(result.IsClamped);
    }

    [Theory]
    [InlineData(0.1, 10, 64)]     // 0.1 MB, 10 seconds, 64 kbps audio
    [InlineData(0.01, 60, 128)]   // 0.01 MB, 1 min, 128 kbps audio
    [InlineData(1.0, 3600, 128)]  // 1 MB, 1 hour, 128 kbps audio
    [InlineData(5.0, 7200, 192)]  // 5 MB, 2 hours, 192 kbps audio
    [InlineData(500.0, 1, 320)]   // 500 MB, 1 second, 320 kbps audio
    public void Calculate_StressParameterGrid_NeverReturnsNegativeBitrate(
        double targetSizeMb,
        double durationSeconds,
        int audioBitrateKbps)
    {
        var duration = TimeSpan.FromSeconds(durationSeconds);
        var result = _calculator.Calculate(targetSizeMb, duration, audioBitrateKbps);

        Assert.True(result.VideoBitrateKbps >= 8,
            $"Expected positive video bitrate, got {result.VideoBitrateKbps}");
        Assert.True(result.AudioBitrateKbps >= 0,
            $"Expected non-negative audio bitrate, got {result.AudioBitrateKbps}");
        Assert.True(result.TotalBitrateKbps > 0,
            $"Expected positive total bitrate, got {result.TotalBitrateKbps}");
    }

    [Fact]
    public void Calculate_MutedAudio_AllocatesFullBudgetToVideo()
    {
        var duration = TimeSpan.FromSeconds(60);
        var result = _calculator.Calculate(targetSizeMb: 10.0, duration: duration, audioBitrateKbps: 0);

        Assert.Equal(0, result.AudioBitrateKbps);
        Assert.Equal(result.TotalBitrateKbps, result.VideoBitrateKbps);
        Assert.True(result.VideoBitrateKbps > 1000);
    }

    [Fact]
    public void CalculateVideoBitrate_Helper_ReturnsConsistentClampedValue()
    {
        var duration = TimeSpan.FromMinutes(5);
        int bitrate = _calculator.CalculateVideoBitrate(targetSizeMb: 0.1, duration: duration, audioBitrateKbps: 128);

        Assert.True(bitrate >= BitrateCalculator.DefaultMinVideoBitrateKbps);
    }
}
