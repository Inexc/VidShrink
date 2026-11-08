namespace VidShrink.Core.Models;

public record AppSettings
{
    public string? FfmpegPath { get; init; }
    public string? DefaultOutputDir { get; init; }
    public string? LastPresetId { get; init; }
    public bool PreferHardware { get; init; } = true;
}
