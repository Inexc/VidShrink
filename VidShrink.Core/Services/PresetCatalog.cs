using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using VidShrink.Core.Models;
using VidShrink.Core.Platform;

namespace VidShrink.Core.Services;

public interface IPresetCatalog
{
    IReadOnlyList<Preset> GetPresets();
    Preset? GetPresetById(string id);
    void SaveUserPresets(IEnumerable<Preset> presets);
    void Reload();
}

public class PresetCatalog : IPresetCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly IPlatformService _platformService;
    private readonly List<Preset> _presets = new();

    public PresetCatalog(IPlatformService? platformService = null)
    {
        _platformService = platformService ?? DefaultPlatformService.Instance;
        Reload();
    }

    public IReadOnlyList<Preset> GetPresets() => _presets.AsReadOnly();

    public Preset? GetPresetById(string id)
    {
        return _presets.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public void Reload()
    {
        _presets.Clear();

        // 1. Try loading from user config directory
        var configDir = _platformService.GetConfigDirectory();
        var userPresetsPath = Path.Combine(configDir, "presets.json");

        if (File.Exists(userPresetsPath))
        {
            try
            {
                var userJson = File.ReadAllText(userPresetsPath);
                var loaded = ParsePresets(userJson);
                if (loaded.Count > 0)
                {
                    _presets.AddRange(loaded);
                    return;
                }
            }
            catch
            {
                // Fallback to embedded if user file is corrupted
            }
        }

        // 2. Load from embedded resource
        var embeddedPresets = LoadEmbeddedPresets();
        if (embeddedPresets.Count > 0)
        {
            _presets.AddRange(embeddedPresets);
            return;
        }

        // 3. Fallback code defaults
        _presets.AddRange(GetFallbackDefaults());
    }

    public void SaveUserPresets(IEnumerable<Preset> presets)
    {
        var configDir = _platformService.GetConfigDirectory();
        var userPresetsPath = Path.Combine(configDir, "presets.json");

        var json = JsonSerializer.Serialize(presets, JsonOptions);
        File.WriteAllText(userPresetsPath, json);
        Reload();
    }

    public static IReadOnlyList<Preset> ParsePresets(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<Preset>();
        var result = JsonSerializer.Deserialize<List<Preset>>(json, JsonOptions);
        return result ?? (IReadOnlyList<Preset>)Array.Empty<Preset>();
    }

    private static IReadOnlyList<Preset> LoadEmbeddedPresets()
    {
        var assembly = Assembly.GetExecutingAssembly();
        const string resourceName = "VidShrink.Core.Presets.presets.json";

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null) return Array.Empty<Preset>();

        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        return ParsePresets(json);
    }

    private static IReadOnlyList<Preset> GetFallbackDefaults()
    {
        return new List<Preset>
        {
            new Preset
            {
                Id = "discord-8mb",
                Name = "Discord Free (8 MB)",
                Description = "Target 8 MB file size limit",
                Mode = CompressionMode.TargetSize,
                TargetSizeMb = 8.0,
                VideoCodec = VideoCodec.H264,
                AudioMode = AudioMode.Aac,
                AudioBitrateKbps = 96,
                TwoPass = true,
                Speed = SpeedPreset.Medium
            },
            new Preset
            {
                Id = "web-balanced",
                Name = "Web Balanced (CRF 23)",
                Description = "Constant quality encoding",
                Mode = CompressionMode.Quality,
                Crf = 23,
                VideoCodec = VideoCodec.H264,
                AudioMode = AudioMode.Aac,
                AudioBitrateKbps = 128,
                TwoPass = false,
                Speed = SpeedPreset.Medium
            }
        };
    }
}
