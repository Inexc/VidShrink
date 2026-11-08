namespace VidShrink.Core.Models;

public record EncoderDescriptor
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required VideoCodec Codec { get; init; }
    public required HardwareAcceleration Acceleration { get; init; }
    public bool IsHardware => Acceleration != HardwareAcceleration.None;
    public string Description { get; init; } = string.Empty;
}
