using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace VolumeController5;

public sealed class AppConfig
{
    public string? PreferredUsbPort { get; set; }
    public string? PreferredBtPort { get; set; }

    // 5 slotů: "MASTER" nebo "spotify.exe" atd.
    public string[] SlotTargets { get; set; } = new string[5]
    {
        "MASTER","MASTER","MASTER","MASTER","MASTER"
    };

    // Uživatelsky připnuté exe – budou vždy vidět v dropdownu.
    public List<string> PinnedExe { get; set; } = new();

    public static string GetConfigPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VolumeController5"
        );
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "config.json");
    }

    public static AppConfig Load()
    {
        try
        {
            var path = GetConfigPath();
            if (!File.Exists(path)) return new AppConfig();
            var json = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();

            cfg.PinnedExe = cfg.PinnedExe
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return cfg;
        }
        catch
        {
            return new AppConfig();
        }
    }

    public void Save()
    {
        var path = GetConfigPath();
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}
