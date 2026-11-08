using System;
using VidShrink.Core.Models;
using VidShrink.Core.Services;
using Xunit;

namespace VidShrink.Tests;

public class ProgressParserTests
{
    [Fact]
    public void FeedLine_SinglePassNormalBlock_EmitsAccurateProgressSnapshot()
    {
        var parser = new ProgressParser();
        parser.Reset(TimeSpan.FromSeconds(60), currentPass: 1, totalPasses: 1);

        parser.FeedLine("frame=120");
        parser.FeedLine("fps=30.0");
        parser.FeedLine("stream_0_0_q=28.0");
        parser.FeedLine("bitrate= 1234.5kbits/s");
        parser.FeedLine("total_size=1048576");
        parser.FeedLine("out_time_us=30000000"); // 30 seconds
        parser.FeedLine("dup_frames=0");
        parser.FeedLine("drop_frames=0");
        parser.FeedLine("speed= 1.50x");

        var snapshot = parser.FeedLine("progress=continue");

        Assert.NotNull(snapshot);
        Assert.Equal(TimeSpan.FromSeconds(30), snapshot.ProcessedTime);
        Assert.Equal(TimeSpan.FromSeconds(60), snapshot.TotalDuration);
        Assert.Equal(50.0, snapshot.Percent);
        Assert.Equal(50.0, snapshot.PassPercent);
        Assert.Equal(30.0, snapshot.Fps);
        Assert.Equal(1.50, snapshot.SpeedRatio);
        Assert.Equal(1048576, snapshot.ProcessedSizeBytes);

        // 30 seconds remaining at 1.5x speed -> 20 seconds ETA
        Assert.NotNull(snapshot.Eta);
        Assert.Equal(20, Math.Round(snapshot.Eta.Value.TotalSeconds));
    }

    [Fact]
    public void FeedLine_TwoPassWorkflow_CalculatesOverallAndPassPercentagesCorrectly()
    {
        var parser = new ProgressParser();

        // Pass 1: 50% through pass 1
        parser.Reset(TimeSpan.FromSeconds(100), currentPass: 1, totalPasses: 2);
        parser.FeedLine("out_time_us=50000000"); // 50s
        parser.FeedLine("speed=2.0x");
        var pass1Mid = parser.FeedLine("progress=continue");

        Assert.NotNull(pass1Mid);
        Assert.Equal(50.0, pass1Mid.PassPercent);
        Assert.Equal(25.0, pass1Mid.Percent); // 50% * 0.5 = 25% overall
        Assert.Equal(1, pass1Mid.CurrentPass);
        Assert.Equal(2, pass1Mid.TotalPasses);

        // ETA in pass 1: remaining in pass 1 (50s) + full pass 2 (100s) = 150s / 2.0 = 75s
        Assert.NotNull(pass1Mid.Eta);
        Assert.Equal(75, Math.Round(pass1Mid.Eta.Value.TotalSeconds));

        // Pass 1: finished
        var pass1End = parser.FeedLine("progress=end");
        Assert.NotNull(pass1End);
        Assert.Equal(100.0, pass1End.PassPercent);
        Assert.Equal(50.0, pass1End.Percent); // 50% overall at end of pass 1

        // Pass 2: 50% through pass 2
        parser.Reset(TimeSpan.FromSeconds(100), currentPass: 2, totalPasses: 2);
        parser.FeedLine("out_time_us=50000000"); // 50s
        parser.FeedLine("speed=2.0x");
        var pass2Mid = parser.FeedLine("progress=continue");

        Assert.NotNull(pass2Mid);
        Assert.Equal(50.0, pass2Mid.PassPercent);
        Assert.Equal(75.0, pass2Mid.Percent); // 50 + 50*0.5 = 75% overall
        // ETA in pass 2: remaining in pass 2 (50s) / 2.0 = 25s
        Assert.NotNull(pass2Mid.Eta);
        Assert.Equal(25, Math.Round(pass2Mid.Eta.Value.TotalSeconds));

        // Pass 2: finished
        var pass2End = parser.FeedLine("progress=end");
        Assert.NotNull(pass2End);
        Assert.Equal(100.0, pass2End.PassPercent);
        Assert.Equal(100.0, pass2End.Percent);
        Assert.Equal(TimeSpan.Zero, pass2End.Eta);
    }

    [Theory]
    [InlineData("speed=N/A")]
    [InlineData("speed=  N/A")]
    [InlineData("speed=0.00x")]
    [InlineData("speed=0")]
    public void FeedLine_UnavailableOrZeroSpeed_SetsSpeedZeroAndEtaNull(string speedLine)
    {
        var parser = new ProgressParser();
        parser.Reset(TimeSpan.FromSeconds(60), currentPass: 1, totalPasses: 1);

        parser.FeedLine("out_time_us=10000000");
        parser.FeedLine(speedLine);
        var snapshot = parser.FeedLine("progress=continue");

        Assert.NotNull(snapshot);
        Assert.Equal(0, snapshot.SpeedRatio);
        Assert.Null(snapshot.Eta);
    }

    [Fact]
    public void FeedLine_NegativeOutTimeUs_ClampsProcessedTimeToZero()
    {
        var parser = new ProgressParser();
        parser.Reset(TimeSpan.FromSeconds(30), currentPass: 1, totalPasses: 1);

        parser.FeedLine("out_time_us=-1500000");
        var snapshot = parser.FeedLine("progress=continue");

        Assert.NotNull(snapshot);
        Assert.Equal(TimeSpan.Zero, snapshot.ProcessedTime);
        Assert.Equal(0.0, snapshot.Percent);
    }

    [Fact]
    public void FeedLine_FallbackToOutTimeFormattedString_ParsesSuccessfully()
    {
        var parser = new ProgressParser();
        parser.Reset(TimeSpan.FromSeconds(100), currentPass: 1, totalPasses: 1);

        parser.FeedLine("out_time=00:00:25.500000");
        var snapshot = parser.FeedLine("progress=continue");

        Assert.NotNull(snapshot);
        Assert.Equal(TimeSpan.FromSeconds(25.5), snapshot.ProcessedTime);
        Assert.Equal(25.5, snapshot.Percent);
    }

    [Fact]
    public void FeedLine_OutTimeFormattedString_UpdatesAcrossMultipleBlocks()
    {
        var parser = new ProgressParser();
        parser.Reset(TimeSpan.FromSeconds(100), currentPass: 1, totalPasses: 1);

        // Block 1
        parser.FeedLine("out_time=00:00:20.000000");
        var s1 = parser.FeedLine("progress=continue");
        Assert.NotNull(s1);
        Assert.Equal(TimeSpan.FromSeconds(20), s1.ProcessedTime);
        Assert.Equal(20.0, s1.Percent);

        // Block 2: must advance and NOT freeze
        parser.FeedLine("out_time=00:00:50.000000");
        var s2 = parser.FeedLine("progress=continue");
        Assert.NotNull(s2);
        Assert.Equal(TimeSpan.FromSeconds(50), s2.ProcessedTime);
        Assert.Equal(50.0, s2.Percent);

        // Block 3
        parser.FeedLine("out_time=00:01:20.000000");
        var s3 = parser.FeedLine("progress=continue");
        Assert.NotNull(s3);
        Assert.Equal(TimeSpan.FromSeconds(80), s3.ProcessedTime);
        Assert.Equal(80.0, s3.Percent);
    }

    [Fact]
    public void FeedLine_OutTimeMs_UpdatesAcrossMultipleBlocks()
    {
        var parser = new ProgressParser();
        parser.Reset(TimeSpan.FromSeconds(60), currentPass: 1, totalPasses: 1);

        // Block 1: 15000 ms = 15s
        parser.FeedLine("out_time_ms=15000");
        var s1 = parser.FeedLine("progress=continue");
        Assert.NotNull(s1);
        Assert.Equal(TimeSpan.FromSeconds(15), s1.ProcessedTime);

        // Block 2: 30000 ms = 30s
        parser.FeedLine("out_time_ms=30000");
        var s2 = parser.FeedLine("progress=continue");
        Assert.NotNull(s2);
        Assert.Equal(TimeSpan.FromSeconds(30), s2.ProcessedTime);
        Assert.Equal(50.0, s2.Percent);
    }

    [Fact]
    public void FeedLine_ExtremeDuration_DoesNotThrowEtaOverflow()
    {
        var parser = new ProgressParser();
        parser.Reset(TimeSpan.FromDays(300), currentPass: 1, totalPasses: 1);

        parser.FeedLine("out_time_us=1000000");
        parser.FeedLine("speed=0.0010001x");
        var snapshot = parser.FeedLine("progress=continue");

        Assert.NotNull(snapshot);
        // ETA might be capped or valid, but must NOT throw OverflowException
    }

    [Fact]
    public void FeedLine_ProgressEnd_ReachesHundredPercentAndZeroEta()
    {
        var parser = new ProgressParser();
        parser.Reset(TimeSpan.FromSeconds(45), currentPass: 1, totalPasses: 1);

        parser.FeedLine("out_time_us=40000000");
        var snapshot = parser.FeedLine("progress=end");

        Assert.NotNull(snapshot);
        Assert.Equal(100.0, snapshot.Percent);
        Assert.Equal(100.0, snapshot.PassPercent);
        Assert.Equal(TimeSpan.Zero, snapshot.Eta);
        Assert.Equal(TimeSpan.FromSeconds(45), snapshot.ProcessedTime);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("random text without equal sign")]
    [InlineData("=no_key")]
    public void FeedLine_MalformedOrEmptyLines_ReturnsNullWithoutThrowing(string malformedLine)
    {
        var parser = new ProgressParser();
        var result = parser.FeedLine(malformedLine);
        Assert.Null(result);
    }

    [Fact]
    public void FeedLine_ZeroTotalDuration_DoesNotThrowDivisionByZero()
    {
        var parser = new ProgressParser();
        parser.Reset(TimeSpan.Zero, currentPass: 1, totalPasses: 1);

        parser.FeedLine("out_time_us=5000000");
        parser.FeedLine("speed=1.0x");
        var snapshot = parser.FeedLine("progress=continue");

        Assert.NotNull(snapshot);
        Assert.Equal(0.0, snapshot.Percent);
        Assert.Null(snapshot.Eta);
    }
}
