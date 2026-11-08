using System;
using System.IO;
using VidShrink.Core.Platform;

namespace VidShrink.Core.Services;

public interface ILogService
{
    string LogsDirectory { get; }
    string LogFfmpegError(string standardError, string? commandContext = null);
}

public class LogService : ILogService
{
    private readonly IPlatformService _platformService;

    public LogService(IPlatformService? platformService = null)
    {
        _platformService = platformService ?? DefaultPlatformService.Instance;
    }

    public string LogsDirectory => Path.Combine(_platformService.GetConfigDirectory(), "logs");

    public string LogFfmpegError(string standardError, string? commandContext = null)
    {
        try
        {
            var dir = LogsDirectory;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var timestamp = DateTime.UtcNow;
            var filename = $"ffmpeg_error_{timestamp:yyyyMMdd_HHmmss}_{Guid.NewGuid():N[..6]}.log";
            var filePath = Path.Combine(dir, filename);

            var tail = ExtractTail(standardError, maxLines: 50);

            var content = $"""
            ================================================================================
            VidShrink FFmpeg Error Log
            Timestamp (UTC): {timestamp:O}
            Platform: {_platformService.CurrentPlatform}
            Command context: {commandContext ?? "N/A"}
            ================================================================================
            Stderr tail:
            {tail}
            """;

            File.WriteAllText(filePath, content);
            return filePath;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ExtractTail(string text, int maxLines)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length <= maxLines) return text;
        return string.Join(Environment.NewLine, lines[^maxLines..]);
    }
}
