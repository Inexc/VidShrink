using System;
using VidShrink.Core.Services;
using Xunit;

namespace VidShrink.Tests;

public class ProbeServiceTests
{
    private readonly ProbeService _probeService = new();

    private const string SampleStandardMp4 = """
    {
      "streams": [
        {
          "index": 0,
          "codec_name": "h264",
          "codec_long_name": "H.264 / AVC / MPEG-4 AVC / MPEG-4 part 10",
          "profile": "High",
          "codec_type": "video",
          "width": 1920,
          "height": 1080,
          "r_frame_rate": "30/1",
          "avg_frame_rate": "30/1",
          "pix_fmt": "yuv420p",
          "duration": "45.500000",
          "bit_rate": "4500000",
          "nb_frames": "1365"
        },
        {
          "index": 1,
          "codec_name": "aac",
          "codec_long_name": "AAC (Advanced Audio Coding)",
          "codec_type": "audio",
          "channels": 2,
          "channel_layout": "stereo",
          "sample_rate": "48000",
          "bit_rate": "192000",
          "duration": "45.500000"
        }
      ],
      "format": {
        "filename": "/videos/test.mp4",
        "format_name": "mov,mp4,m4a,3gp,3g2,mj2",
        "duration": "45.500000",
        "size": "26738688",
        "bit_rate": "4701307"
      }
    }
    """;

    private const string SampleRotatedPhoneVideo = """
    {
      "streams": [
        {
          "index": 0,
          "codec_name": "hevc",
          "codec_type": "video",
          "width": 3840,
          "height": 2160,
          "r_frame_rate": "60/1",
          "duration": "10.000000",
          "tags": {
            "rotate": "90"
          }
        }
      ],
      "format": {
        "filename": "phone.mp4",
        "format_name": "mov,mp4",
        "duration": "10.000000",
        "size": "50000000",
        "bit_rate": "40000000"
      }
    }
    """;

    private const string SampleSideDataRotation = """
    {
      "streams": [
        {
          "index": 0,
          "codec_name": "h264",
          "codec_type": "video",
          "width": 1280,
          "height": 720,
          "r_frame_rate": "24000/1001",
          "side_data_list": [
            {
              "side_data_type": "Display Matrix",
              "rotation": -90
            }
          ]
        }
      ],
      "format": {
        "filename": "camera.mp4",
        "format_name": "mov,mp4",
        "duration": "20.000000",
        "size": "10000000"
      }
    }
    """;

    private const string SampleMissingStreamBitrate = """
    {
      "streams": [
        {
          "index": 0,
          "codec_name": "vp9",
          "codec_type": "video",
          "width": 1920,
          "height": 1080,
          "r_frame_rate": "25/1"
        }
      ],
      "format": {
        "filename": "web.webm",
        "format_name": "matroska,webm",
        "duration": "30.000000",
        "size": "15000000",
        "bit_rate": "4000000"
      }
    }
    """;

    private const string SampleFfprobeError = """
    {
      "error": {
        "code": -1094995529,
        "string": "Invalid data found when processing input"
      }
    }
    """;

    [Fact]
    public void ParseJson_StandardMp4_ExtractsAccurateProperties()
    {
        var mediaInfo = _probeService.ParseJson(SampleStandardMp4);

        Assert.Equal(26738688, mediaInfo.FileSizeBytes);
        Assert.Equal(TimeSpan.FromSeconds(45.5), mediaInfo.Duration);
        Assert.Equal(4701307, mediaInfo.Bitrate);
        Assert.Equal("mov,mp4,m4a,3gp,3g2,mj2", mediaInfo.FormatName);

        Assert.True(mediaInfo.HasVideo);
        Assert.True(mediaInfo.HasAudio);

        var video = mediaInfo.PrimaryVideoStream;
        Assert.NotNull(video);
        Assert.Equal("h264", video.CodecName);
        Assert.Equal(1920, video.Width);
        Assert.Equal(1080, video.Height);
        Assert.Equal(30.0, video.FrameRate);
        Assert.Equal(0, video.Rotation);
        Assert.Equal(1365, video.TotalFrames);

        var audio = mediaInfo.PrimaryAudioStream;
        Assert.NotNull(audio);
        Assert.Equal("aac", audio.CodecName);
        Assert.Equal(2, audio.Channels);
        Assert.Equal(48000, audio.SampleRate);
        Assert.Equal(192000, audio.Bitrate);
    }

    [Fact]
    public void ParseJson_RotatedPhoneVideo_ExtractsRotationTag()
    {
        var mediaInfo = _probeService.ParseJson(SampleRotatedPhoneVideo);

        Assert.NotNull(mediaInfo.PrimaryVideoStream);
        Assert.Equal(90, mediaInfo.PrimaryVideoStream.Rotation);
        Assert.Equal(60.0, mediaInfo.PrimaryVideoStream.FrameRate);
        Assert.Equal("hevc", mediaInfo.PrimaryVideoStream.CodecName);
    }

    [Fact]
    public void ParseJson_SideDataRotation_NormalizesNegativeRotation()
    {
        var mediaInfo = _probeService.ParseJson(SampleSideDataRotation);

        Assert.NotNull(mediaInfo.PrimaryVideoStream);
        // -90 normalized is 270
        Assert.Equal(270, mediaInfo.PrimaryVideoStream.Rotation);
        // 24000/1001 = 23.976 fps
        Assert.Equal(23.976, mediaInfo.PrimaryVideoStream.FrameRate);
    }

    [Fact]
    public void ParseJson_MissingStreamBitrate_FallsBackToFormatBitrate()
    {
        var mediaInfo = _probeService.ParseJson(SampleMissingStreamBitrate);

        Assert.Equal(4000000, mediaInfo.Bitrate);
        Assert.NotNull(mediaInfo.PrimaryVideoStream);
        Assert.Equal(0, mediaInfo.PrimaryVideoStream.Bitrate);
        Assert.Equal("vp9", mediaInfo.PrimaryVideoStream.CodecName);
    }

    [Fact]
    public void ParseJson_FfprobeErrorPayload_ThrowsDescriptiveInvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            _probeService.ParseJson(SampleFfprobeError));

        Assert.Contains("Invalid data found", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ParseJson_EmptyOrNullJson_ThrowsArgumentException(string? invalidJson)
    {
        Assert.Throws<ArgumentException>(() =>
            _probeService.ParseJson(invalidJson!));
    }
}
