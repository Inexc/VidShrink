using System;
using System.Globalization;
using VidShrink.Core.Models;

namespace VidShrink.Core.Services;

public class ProgressParser
{
    private TimeSpan _processedTime = TimeSpan.Zero;
    private double _fps;
    private double _speed;
    private long _totalSize;
    private bool _timeParsedInCurrentBlock;

    public TimeSpan TotalDuration { get; set; } = TimeSpan.Zero;
    public int CurrentPass { get; set; } = 1;
    public int TotalPasses { get; set; } = 1;

    public ProgressInfo CurrentProgress => BuildProgressInfo(false);

    public void Reset(TimeSpan totalDuration, int currentPass = 1, int totalPasses = 1)
    {
        TotalDuration = totalDuration > TimeSpan.Zero ? totalDuration : TimeSpan.Zero;
        CurrentPass = Math.Max(1, currentPass);
        TotalPasses = Math.Max(1, totalPasses);
        _processedTime = TimeSpan.Zero;
        _fps = 0;
        _speed = 0;
        _totalSize = 0;
        _timeParsedInCurrentBlock = false;
    }

    public ProgressInfo? FeedLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        int equalIndex = line.IndexOf('=');
        if (equalIndex <= 0)
            return null;

        var key = line.Substring(0, equalIndex).Trim();
        var val = line.Substring(equalIndex + 1).Trim();

        switch (key)
        {
            case "out_time_us":
                if (long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var us))
                {
                    _processedTime = us > 0 ? TimeSpan.FromTicks(us * 10) : TimeSpan.Zero;
                    _timeParsedInCurrentBlock = true;
                }
                break;

            case "out_time_ms":
                // In some ffmpeg versions out_time_ms is in microseconds, in others in milliseconds.
                // Only use if out_time_us was not parsed in the current progress block.
                if (!_timeParsedInCurrentBlock &&
                    long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var msVal) && msVal > 0)
                {
                    _processedTime = msVal > 10_000_000
                        ? TimeSpan.FromTicks(msVal * 10) // likely microseconds
                        : TimeSpan.FromMilliseconds(msVal);
                    _timeParsedInCurrentBlock = true;
                }
                break;

            case "out_time":
                // Only use if out_time_us or out_time_ms was not parsed in the current progress block.
                if (!_timeParsedInCurrentBlock &&
                    TimeSpan.TryParse(val, CultureInfo.InvariantCulture, out var parsedTime) &&
                    parsedTime > TimeSpan.Zero)
                {
                    _processedTime = parsedTime;
                    _timeParsedInCurrentBlock = true;
                }
                break;

            case "fps":
                if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var fps))
                {
                    _fps = Math.Max(0, fps);
                }
                break;

            case "speed":
                var speedStr = val;
                if (speedStr.EndsWith("x", StringComparison.OrdinalIgnoreCase))
                {
                    speedStr = speedStr[..^1];
                }
                speedStr = speedStr.Trim();

                if (double.TryParse(speedStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var speed))
                {
                    _speed = Math.Max(0, speed);
                }
                else
                {
                    _speed = 0;
                }
                break;

            case "total_size":
                if (long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size))
                {
                    _totalSize = Math.Max(0, size);
                }
                break;

            case "progress":
                _timeParsedInCurrentBlock = false;
                bool isEnd = string.Equals(val, "end", StringComparison.OrdinalIgnoreCase);
                return BuildProgressInfo(isEnd);
        }

        return null;
    }

    private ProgressInfo BuildProgressInfo(bool isEnd)
    {
        double passPercent;
        if (isEnd)
        {
            passPercent = 100.0;
        }
        else if (TotalDuration > TimeSpan.Zero)
        {
            passPercent = Math.Clamp((_processedTime.TotalSeconds / TotalDuration.TotalSeconds) * 100.0, 0.0, 100.0);
        }
        else
        {
            passPercent = 0.0;
        }

        double overallPercent;
        if (TotalPasses == 2)
        {
            if (isEnd && CurrentPass == 2)
            {
                overallPercent = 100.0;
            }
            else if (isEnd && CurrentPass == 1)
            {
                overallPercent = 50.0;
            }
            else if (CurrentPass == 1)
            {
                overallPercent = passPercent * 0.5;
            }
            else
            {
                overallPercent = 50.0 + (passPercent * 0.5);
            }
        }
        else
        {
            overallPercent = isEnd ? 100.0 : passPercent;
        }

        TimeSpan? eta = null;
        if (isEnd)
        {
            eta = TimeSpan.Zero;
        }
        else if (_speed > 0.001 && TotalDuration > TimeSpan.Zero)
        {
            var remainingInPassSec = Math.Max(0.0, TotalDuration.TotalSeconds - _processedTime.TotalSeconds);
            double totalRemainingSec;
            if (TotalPasses == 2 && CurrentPass == 1)
            {
                totalRemainingSec = (remainingInPassSec + TotalDuration.TotalSeconds) / _speed;
            }
            else
            {
                totalRemainingSec = remainingInPassSec / _speed;
            }

            if (!double.IsNaN(totalRemainingSec) && !double.IsInfinity(totalRemainingSec) &&
                totalRemainingSec >= 0 && totalRemainingSec < 86400 * 365) // safe upper bound (1 year)
            {
                eta = TimeSpan.FromSeconds(totalRemainingSec);
            }
        }

        var processedTime = (isEnd && TotalDuration > TimeSpan.Zero && _processedTime < TotalDuration)
            ? TotalDuration
            : _processedTime;

        return new ProgressInfo
        {
            ProcessedTime = processedTime,
            TotalDuration = TotalDuration,
            Percent = Math.Round(overallPercent, 1),
            PassPercent = Math.Round(passPercent, 1),
            Fps = Math.Round(_fps, 1),
            SpeedRatio = Math.Round(_speed, 2),
            Eta = eta,
            CurrentPass = CurrentPass,
            TotalPasses = TotalPasses,
            ProcessedSizeBytes = _totalSize
        };
    }
}
