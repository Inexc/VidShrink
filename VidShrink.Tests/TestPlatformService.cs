using System.Collections.Generic;
using VidShrink.Core.Platform;

namespace VidShrink.Tests;

public class TestPlatformService : IPlatformService
{
    public PlatformKind CurrentPlatform { get; set; } = PlatformKind.Windows;
    public bool IsWindows => CurrentPlatform == PlatformKind.Windows;
    public bool IsLinux => CurrentPlatform == PlatformKind.Linux;
    public bool IsMacOS => CurrentPlatform == PlatformKind.MacOS;

    public string NullDevice => IsWindows ? "NUL" : "/dev/null";

    public string GetExecutableName(string baseName) => IsWindows ? $"{baseName}.exe" : baseName;

    public string ConfigDirectory { get; set; } = "/mock/config/VidShrink";
    public string GetConfigDirectory() => ConfigDirectory;

    public string GetOpenFolderCommand()
    {
        if (IsWindows) return "explorer";
        if (IsMacOS) return "open";
        return "xdg-open";
    }

    public IReadOnlyList<string> GetOpenFolderArguments(string targetPath)
    {
        if (IsWindows) return new[] { $"/select,{targetPath}" };
        return new[] { targetPath };
    }
}
