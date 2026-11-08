using System;
using System.IO;
using System.Text.Json;
using VidShrink.Core.Models;
using VidShrink.Core.Platform;

namespace VidShrink.Core.Services;

public interface ISettingsService
{
    AppSettings CurrentSettings { get; }
    AppSettings Load();
    void Save(AppSettings settings);
    string SettingsFilePath { get; }
}

public class SettingsService : ISettingsService
{
    private readonly IPlatformService _platformService;
    private AppSettings _currentSettings;
    private readonly object _lock = new();

    public SettingsService(IPlatformService? platformService = null)
    {
        _platformService = platformService ?? DefaultPlatformService.Instance;
        _currentSettings = Load();
    }

    public string SettingsFilePath => Path.Combine(_platformService.GetConfigDirectory(), "settings.json");

    public AppSettings CurrentSettings
    {
        get
        {
            lock (_lock)
            {
                return _currentSettings;
            }
        }
    }

    public AppSettings Load()
    {
        lock (_lock)
        {
            try
            {
                var path = SettingsFilePath;
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    if (loaded != null)
                    {
                        _currentSettings = loaded;
                        return loaded;
                    }
                }
            }
            catch
            {
                // Fallback to default on invalid json or read error
            }

            _currentSettings = new AppSettings();
            return _currentSettings;
        }
    }

    public void Save(AppSettings settings)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));

        lock (_lock)
        {
            var path = SettingsFilePath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(path, json);
            _currentSettings = settings;
        }
    }
}
