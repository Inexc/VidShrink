namespace VidShrink.Core.Models;

public enum VideoCodec
{
    H264,
    H265,
    AV1,
    VP9
}

public enum HardwareAcceleration
{
    None,
    Nvenc,
    Qsv,
    Amf,
    Vaapi
}

public enum CompressionMode
{
    TargetSize,
    TargetBitrate,
    Quality
}

public enum AudioMode
{
    Aac,
    Opus,
    Copy,
    Mute
}

public enum SpeedPreset
{
    UltraFast,
    SuperFast,
    VeryFast,
    Faster,
    Fast,
    Medium,
    Slow,
    Slower,
    VerySlow
}
