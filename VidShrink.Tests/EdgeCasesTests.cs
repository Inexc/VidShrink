using System;
using System.IO;
using System.Threading.Tasks;
using VidShrink.Core.Models;
using VidShrink.Core.Services;
using Xunit;

namespace VidShrink.Tests;

public class EdgeCasesTests : IDisposable
{
    private readonly string _tempDir;

    public EdgeCasesTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"VidShrinkEdgeCases_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task JobQueue_EmptyQueue_CompletesCleanly()
    {
        var mockEncoder = new MockEncoderService();
        using var queue = new JobQueue(mockEncoder);

        bool completedFired = false;
        queue.QueueCompleted += (_, _) => completedFired = true;

        await queue.StartAsync();

        Assert.True(completedFired);
        Assert.False(queue.IsRunning);
    }

    [Fact]
    public void JobQueue_RemoveJob_RemovesCorrectJob()
    {
        var mockEncoder = new MockEncoderService();
        using var queue = new JobQueue(mockEncoder);

        var job1 = new CompressionJob { InputFilePath = "1.mp4", OutputFilePath = "1_out.mp4", Settings = new CompressionSettings { InputFilePath = "1.mp4", OutputFilePath = "1_out.mp4" } };
        var job2 = new CompressionJob { InputFilePath = "2.mp4", OutputFilePath = "2_out.mp4", Settings = new CompressionSettings { InputFilePath = "2.mp4", OutputFilePath = "2_out.mp4" } };

        queue.Enqueue(job1);
        queue.Enqueue(job2);

        var removed = queue.Remove(job1.Id);
        Assert.True(removed);
        Assert.Single(queue.Jobs);
        Assert.Equal(job2.Id, queue.Jobs[0].Id);
    }

    [Fact]
    public async Task EncoderService_MissingInputFile_ThrowsFileNotFoundException()
    {
        var locator = new MockFfmpegLocator();
        var service = new EncoderService(locator: locator);

        var settings = new CompressionSettings
        {
            InputFilePath = Path.Combine(_tempDir, "does_not_exist.mp4"),
            OutputFilePath = Path.Combine(_tempDir, "out.mp4")
        };

        await Assert.ThrowsAsync<FileNotFoundException>(() => service.EncodeAsync(settings));
    }

    [Fact]
    public async Task EncoderService_MissingFfmpeg_ThrowsFileNotFoundException()
    {
        var locator = new MockFfmpegLocator { FfmpegPath = null };
        var service = new EncoderService(locator: locator);

        var dummyInput = Path.Combine(_tempDir, "input.mp4");
        File.WriteAllText(dummyInput, "data");

        var settings = new CompressionSettings
        {
            InputFilePath = dummyInput,
            OutputFilePath = Path.Combine(_tempDir, "out.mp4")
        };

        await Assert.ThrowsAsync<FileNotFoundException>(() => service.EncodeAsync(settings));
    }

    [Fact]
    public void PathHelper_WhenCollisionExists_IncrementsCounterUntilUnique()
    {
        var input = Path.Combine(_tempDir, "video.mp4");
        File.WriteAllText(input, "original");

        var outDefault = Path.Combine(_tempDir, "video_compressed.mp4");
        File.WriteAllText(outDefault, "collision 0");

        var out1 = Path.Combine(_tempDir, "video_compressed_1.mp4");
        File.WriteAllText(out1, "collision 1");

        // Next candidate should be video_compressed_2.mp4
        var uniquePath = PathHelper.GenerateUniqueOutputPath(input);

        Assert.Equal(Path.Combine(_tempDir, "video_compressed_2.mp4"), uniquePath);
    }

    [Fact]
    public void PathHelper_WhenCandidateMatchesInput_AppendsCounterToAvoidOverwrite()
    {
        var input = Path.Combine(_tempDir, "video.mp4");
        File.WriteAllText(input, "input");

        // When suffix is empty, first candidate is video.mp4, which matches input
        var generated = PathHelper.GenerateUniqueOutputPath(input, suffix: "");

        // Candidate must never equal the input file
        Assert.NotEqual(input, generated);
        Assert.Equal(Path.Combine(_tempDir, "video_1.mp4"), generated);
    }

    [Fact]
    public void ProgressParser_MultipleSequentialBlocks_MaintainsLatestState()
    {
        var parser = new ProgressParser();
        parser.Reset(TimeSpan.FromSeconds(100), currentPass: 1, totalPasses: 1);

        // Block 1
        parser.FeedLine("out_time_us=10000000"); // 10s
        parser.FeedLine("speed=1.0x");
        var b1 = parser.FeedLine("progress=continue");

        Assert.NotNull(b1);
        Assert.Equal(10.0, b1.Percent);

        // Block 2
        parser.FeedLine("out_time_us=30000000"); // 30s
        parser.FeedLine("speed=2.0x");
        var b2 = parser.FeedLine("progress=continue");

        Assert.NotNull(b2);
        Assert.Equal(30.0, b2.Percent);
        Assert.Equal(2.0, b2.SpeedRatio);

        // Block 3: end
        var b3 = parser.FeedLine("progress=end");
        Assert.NotNull(b3);
        Assert.Equal(100.0, b3.Percent);
        Assert.Equal(TimeSpan.Zero, b3.Eta);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void PathHelper_NullOrWhitespaceInput_ThrowsArgumentException(string? invalidInput)
    {
        Assert.Throws<ArgumentException>(() => PathHelper.GenerateUniqueOutputPath(invalidInput!));
    }
}
