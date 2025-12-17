using System;
using System.IO;
using System.Text.Json;

namespace Disc.Analyzer.Services;

public class AppSettings
{
    public bool ShowFiles { get; set; } = false;
    public int DefaultExpansionLevel { get; set; } = 1;
    public int MaxParallelism { get; set; } = Environment.ProcessorCount;
    public string? LastScannedPath { get; set; }
    public bool ShowSystemVolumes { get; set; } = false;
    public double WindowWidth { get; set; } = 1000;
    public double WindowHeight { get; set; } = 700;
    public bool AutoUpdate { get; set; } = true;
}

public class SettingsService
{
    private static readonly string SettingsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DiscAnalyzer");
    
    private static readonly string SettingsFile = Path.Combine(SettingsFolder, "settings.json");
    
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AppSettings Settings { get; private set; } = new();

    public SettingsService()
    {
        Load();
    }

    public void Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                var json = File.ReadAllText(SettingsFile);
                Settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception)
        {
            // If loading fails, use defaults
            Settings = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            // Ensure directory exists
            Directory.CreateDirectory(SettingsFolder);
            
            var json = JsonSerializer.Serialize(Settings, JsonOptions);
            File.WriteAllText(SettingsFile, json);
        }
        catch (Exception)
        {
            // Silently fail if we can't save settings
        }
    }

    public void UpdateAndSave(Action<AppSettings> update)
    {
        update(Settings);
        Save();
    }
}
