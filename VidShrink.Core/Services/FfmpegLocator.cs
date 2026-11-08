using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using VidShrink.Core.Platform;

namespace VidShrink.Core.Services;

public record FfmpegPaths
{
    public string? FfmpegPath { get; init; }
    public string? FfprobePath { get; init; }
    public bool IsFfmpegAvailable => !string.IsNullOrEmpty(FfmpegPath) && File.Exists(FfmpegPath);
    public bool IsFfprobeAvailable => !string.IsNullOrEmpty(FfprobePath) && File.Exists(FfprobePath);
    public bool IsFullyAvailable => IsFfmpegAvailable && IsFfprobeAvailable;
    public string? FfmpegVersion { get; init; }
    public string? FfprobeVersion { get; init; }
}

public interface IFfmpegLocator
{
    FfmpegPaths Locate(string? customFfmpegDirectory = null);
    string? FindExecutable(string baseName, string? customDirectory = null);
    string? GetVersion(string executablePath);
}

public class FfmpegLocator : IFfmpegLocator
{
    private static readonly ConcurrentDictionary<string, string?> _versionCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly IPlatformService _platformService;
    private readonly ISettingsService? _settingsService;

    public FfmpegLocator(IPlatformService? platformService = null, ISettingsService? settingsService = null)
    {
        _platformService = platformService ?? DefaultPlatformService.Instance;
        _settingsService = settingsService;
    }

    public static void ClearVersionCache() => _versionCache.Clear();

    public FfmpegPaths Locate(string? customFfmpegDirectory = null)
    {
        var customPath = !string.IsNullOrWhiteSpace(customFfmpegDirectory)
            ? customFfmpegDirectory
            : _settingsService?.CurrentSettings.FfmpegPath;

        var ffmpeg = FindExecutable("ffmpeg", customPath);
        var ffprobe = FindExecutable("ffprobe", customPath);

        string? ffmpegVer = ffmpeg != null ? GetVersion(ffmpeg) : null;
        string? ffprobeVer = ffprobe != null ? GetVersion(ffprobe) : null;

        return new FfmpegPaths
        {
            FfmpegPath = ffmpeg,
            FfprobePath = ffprobe,
            FfmpegVersion = ffmpegVer,
            FfprobeVersion = ffprobeVer
        };
    }

    public string? FindExecutable(string baseName, string? customDirectory = null)
    {
        string execName = _platformService.GetExecutableName(baseName);
        var targetCustom = !string.IsNullOrWhiteSpace(customDirectory)
            ? customDirectory
            : _settingsService?.CurrentSettings.FfmpegPath;

        // 1. Check custom path (either file or directory) if specified
        if (!string.IsNullOrWhiteSpace(targetCustom))
        {
            if (File.Exists(targetCustom))
            {
                if (Path.GetFileNameWithoutExtension(targetCustom).Equals(baseName, StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetFullPath(targetCustom);
                }

                var parent = Path.GetDirectoryName(targetCustom);
                if (!string.IsNullOrWhiteSpace(parent))
                {
                    var peer = Path.Combine(parent, execName);
                    if (File.Exists(peer))
                        return Path.GetFullPath(peer);
                }
            }
            else if (Directory.Exists(targetCustom))
            {
                var candidate = Path.Combine(targetCustom, execName);
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
        }

        // 2. Check adjacent tools directory: tools/ffmpeg, tools, or app directory
        var appDir = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(appDir) && Directory.Exists(appDir))
        {
            var candidates = new[]
            {
                Path.Combine(appDir, "tools", "ffmpeg", execName),
                Path.Combine(appDir, "tools", execName),
                Path.Combine(appDir, "ffmpeg", execName),
                Path.Combine(appDir, execName),
                Path.Combine(appDir, "bin", execName)
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
        }

        // 3. Search in PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathEnv))
        {
            var separator = Path.PathSeparator;
            var dirs = pathEnv.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var rawDir in dirs)
            {
                var dir = rawDir.Trim().Trim('"', '\'');
                if (string.IsNullOrEmpty(dir)) continue;

                try
                {
                    if (Directory.Exists(dir))
                    {
                        var candidate = Path.Combine(dir, execName);
                        if (File.Exists(candidate))
                            return Path.GetFullPath(candidate);
                    }
                }
                catch
                {
                    // Ignore inaccessible or invalid paths in PATH
                }
            }
        }

        // 4. Linux / Unix common fallback paths if not in PATH
        if (_platformService.IsLinux || _platformService.IsMacOS)
        {
            var standardPaths = new[]
            {
                "/usr/bin",
                "/usr/local/bin",
                "/bin",
                "/snap/bin",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin")
            };

            foreach (var dir in standardPaths)
            {
                if (Directory.Exists(dir))
                {
                    var candidate = Path.Combine(dir, execName);
                    if (File.Exists(candidate))
                        return Path.GetFullPath(candidate);
                }
            }
        }

        return null;
    }

    public string? GetVersion(string executablePath)
    {
        if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
            return null;

        var fullPath = Path.GetFullPath(executablePath);
        return _versionCache.GetOrAdd(fullPath, QueryVersion);
    }

    private static string? QueryVersion(string executablePath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = executablePath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-version");

            using var process = Process.Start(psi);
            if (process == null) return null;

            var stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);

            if (string.IsNullOrWhiteSpace(stdout)) return null;

            // First line typically: "ffmpeg version 6.1.1 Copyright (c) 2000-2023..."
            using var reader = new StringReader(stdout);
            var firstLine = reader.ReadLine();
            return firstLine?.Trim();
        }
        catch
        {
            return null;
        }
    }
}
