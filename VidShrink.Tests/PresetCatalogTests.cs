using System.Linq;
using VidShrink.Core.Models;
using VidShrink.Core.Services;
using Xunit;

namespace VidShrink.Tests;

public class PresetCatalogTests
{
    private readonly TestPlatformService _testPlatform = new();

    [Fact]
    public void GetPresets_LoadsEmbeddedPresetsSuccessfully()
    {
        var catalog = new PresetCatalog(_testPlatform);
        var presets = catalog.GetPresets();

        Assert.NotEmpty(presets);
        Assert.True(presets.Count >= 5);
    }

    [Fact]
    public void GetPresets_ContainsRequiredDiscordPresets()
    {
        var catalog = new PresetCatalog(_testPlatform);

        var discord8Mb = catalog.GetPresetById("discord-8mb");
        Assert.NotNull(discord8Mb);
        Assert.Equal(8.0, discord8Mb.TargetSizeMb);
        Assert.Equal(CompressionMode.TargetSize, discord8Mb.Mode);
        Assert.True(discord8Mb.TwoPass);
        Assert.Equal(VideoCodec.H264, discord8Mb.VideoCodec);

        var discord25Mb = catalog.GetPresetById("discord-25mb");
        Assert.NotNull(discord25Mb);
        Assert.Equal(25.0, discord25Mb.TargetSizeMb);

        var discord50Mb = catalog.GetPresetById("discord-50mb");
        Assert.NotNull(discord50Mb);
        Assert.Equal(50.0, discord50Mb.TargetSizeMb);
    }

    [Fact]
    public void GetPresets_ContainsAllRequiredSpecificationPresets()
    {
        var catalog = new PresetCatalog(_testPlatform);
        var presets = catalog.GetPresets();

        var names = presets.Select(p => p.Name).ToList();

        Assert.Contains("Для мессенджера", names);
        Assert.Contains("Discord", names);
        Assert.Contains("Почта", names);
        Assert.Contains("Максимальное качество", names);
        Assert.Contains("Баланс", names);
        Assert.Contains("Минимальный размер", names);
        Assert.Contains("Своё", names);

        // Check target modes
        var messenger = presets.First(p => p.Name == "Для мессенджера");
        Assert.Equal(CompressionMode.TargetSize, messenger.Mode);
        Assert.True(messenger.TargetSizeMb.HasValue && messenger.TargetSizeMb.Value > 0);

        var balance = presets.First(p => p.Name == "Баланс");
        Assert.Equal(CompressionMode.Quality, balance.Mode);
        Assert.True(balance.Crf.HasValue && balance.Crf.Value > 0);

        var maxQuality = presets.First(p => p.Name == "Максимальное качество");
        Assert.Equal(CompressionMode.Quality, maxQuality.Mode);
        Assert.True(maxQuality.Crf.HasValue && maxQuality.Crf.Value > 0);
    }

    [Fact]
    public void GetPresets_AllPresetsHaveValidProperties()
    {
        var catalog = new PresetCatalog(_testPlatform);
        var presets = catalog.GetPresets();

        foreach (var preset in presets)
        {
            Assert.False(string.IsNullOrWhiteSpace(preset.Id));
            Assert.False(string.IsNullOrWhiteSpace(preset.Name));

            if (preset.Mode == CompressionMode.TargetSize)
            {
                Assert.True(preset.TargetSizeMb.HasValue && preset.TargetSizeMb.Value > 0);
            }
            else if (preset.Mode == CompressionMode.Quality)
            {
                Assert.True(preset.Crf.HasValue && preset.Crf.Value > 0);
            }
        }
    }

    [Fact]
    public void GetPresetById_CaseInsensitive_FindsPreset()
    {
        var catalog = new PresetCatalog(_testPlatform);

        var lower = catalog.GetPresetById("discord-8mb");
        var upper = catalog.GetPresetById("DISCORD-8MB");

        Assert.NotNull(lower);
        Assert.NotNull(upper);
        Assert.Equal(lower.Id, upper.Id);
    }

    [Fact]
    public void ParsePresets_ValidJsonString_DeserializesCorrectly()
    {
        const string json = """
        [
          {
            "id": "custom-preset",
            "name": "Custom 12 MB",
            "mode": "TargetSize",
            "targetSizeMb": 12.0,
            "videoCodec": "AV1",
            "audioMode": "Opus",
            "audioBitrateKbps": 64,
            "twoPass": true,
            "speed": "Fast"
          }
        ]
        """;

        var presets = PresetCatalog.ParsePresets(json);

        Assert.Single(presets);
        var p = presets[0];
        Assert.Equal("custom-preset", p.Id);
        Assert.Equal(12.0, p.TargetSizeMb);
        Assert.Equal(VideoCodec.AV1, p.VideoCodec);
        Assert.Equal(AudioMode.Opus, p.AudioMode);
        Assert.Equal(SpeedPreset.Fast, p.Speed);
    }
}
