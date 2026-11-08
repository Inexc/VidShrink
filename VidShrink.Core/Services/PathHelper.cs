using System;
using System.IO;

namespace VidShrink.Core.Services;

public static class PathHelper
{
    public static string GenerateUniqueOutputPath(
        string inputFilePath,
        string? targetDirectory = null,
        string suffix = "_compressed",
        string extension = ".mp4")
    {
        if (string.IsNullOrWhiteSpace(inputFilePath))
            throw new ArgumentException("Input file path must not be null or whitespace.", nameof(inputFilePath));

        var dir = !string.IsNullOrWhiteSpace(targetDirectory)
            ? targetDirectory
            : Path.GetDirectoryName(inputFilePath) ?? Directory.GetCurrentDirectory();

        var baseName = Path.GetFileNameWithoutExtension(inputFilePath);
        if (!extension.StartsWith("."))
        {
            extension = "." + extension;
        }

        var fullInput = Path.GetFullPath(inputFilePath);

        var candidate = Path.Combine(dir, $"{baseName}{suffix}{extension}");
        var fullCandidate = Path.GetFullPath(candidate);

        if (!File.Exists(candidate) && !string.Equals(fullCandidate, fullInput, StringComparison.OrdinalIgnoreCase))
        {
            return candidate;
        }

        int counter = 1;
        while (true)
        {
            candidate = Path.Combine(dir, $"{baseName}{suffix}_{counter}{extension}");
            fullCandidate = Path.GetFullPath(candidate);

            if (!File.Exists(candidate) && !string.Equals(fullCandidate, fullInput, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
            counter++;
        }
    }
}
