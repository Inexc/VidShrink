using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace VidShrink.Core.Platform;

public enum PlatformKind
{
    Windows,
    Linux,
    MacOS,
    Unknown
}

public interface IPlatformService
{
    PlatformKind CurrentPlatform { get; }
    bool IsWindows { get; }
    bool IsLinux { get; }
    bool IsMacOS { get; }

    string NullDevice { get; }
    string GetExecutableName(string baseName);
    string GetConfigDirectory();
    string GetOpenFolderCommand();
    IReadOnlyList<string> GetOpenFolderArguments(string targetPath);
}

public class DefaultPlatformService : IPlatformService
{
    public static readonly DefaultPlatformService Instance = new();

    public PlatformKind CurrentPlatform
    {
        get
        {
            if (OperatingSystem.IsWindows()) return PlatformKind.Windows;
            if (OperatingSystem.IsLinux()) return PlatformKind.Linux;
            if (OperatingSystem.IsMacOS()) return PlatformKind.MacOS;
            return PlatformKind.Unknown;
        }
    }

    public bool IsWindows => OperatingSystem.IsWindows();
    public bool IsLinux => OperatingSystem.IsLinux();
    public bool IsMacOS => OperatingSystem.IsMacOS();

    public string NullDevice => IsWindows ? "NUL" : "/dev/null";

    public string GetExecutableName(string baseName)
    {
        if (string.IsNullOrWhiteSpace(baseName))
            throw new ArgumentException("Base name cannot be empty.", nameof(baseName));

        return IsWindows ? $"{baseName}.exe" : baseName;
    }

    public string GetConfigDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData))
        {
            // Fallback for isolated Linux / minimal environments
            var home = Environment.GetEnvironmentVariable("HOME");
            appData = !string.IsNullOrEmpty(home) ? Path.Combine(home, ".config") : Directory.GetCurrentDirectory();
        }

        var configPath = Path.Combine(appData, "VidShrink");
        if (!Directory.Exists(configPath))
        {
            try
            {
                Directory.CreateDirectory(configPath);
            }
            catch
            {
                // Ignore failure if running in read-only sandbox or test
            }
        }

        return configPath;
    }

    public string GetOpenFolderCommand()
    {
        if (IsWindows) return "explorer";
        if (IsMacOS) return "open";
        return "xdg-open";
    }

    public IReadOnlyList<string> GetOpenFolderArguments(string targetPath)
    {
        var args = new List<string>();

        if (IsWindows)
        {
            // If it's a file, explorer /select, "path" selects it. If directory, opens it.
            if (File.Exists(targetPath))
            {
                args.Add($"/select,{targetPath}");
            }
            else
            {
                args.Add(targetPath);
            }
        }
        else if (IsMacOS)
        {
            if (File.Exists(targetPath))
            {
                args.Add("-R");
                args.Add(targetPath);
            }
            else
            {
                args.Add(targetPath);
            }
        }
        else
        {
            // Linux xdg-open opens directory
            var dir = File.Exists(targetPath) ? Path.GetDirectoryName(targetPath) ?? targetPath : targetPath;
            args.Add(dir);
        }

        return args;
    }
}
