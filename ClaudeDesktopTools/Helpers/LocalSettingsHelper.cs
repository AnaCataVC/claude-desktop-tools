using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;

namespace ClaudeDesktopTools.Helpers;

public static class LocalSettingsHelper
{
    private static readonly object _fileLock = new();
    private static string? _customPath;

    public static string SettingsFilePath
    {
        get
        {
            if (!string.IsNullOrEmpty(_customPath))
                return _customPath;

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appDir = Path.Combine(localAppData, "ClaudeDesktopTools");
            Directory.CreateDirectory(appDir);
            return Path.Combine(appDir, "LocalSettings.json");
        }
        set => _customPath = value;
    }

    public static void ResetToDefaultPath()
    {
        _customPath = null;
    }

    private static Dictionary<string, string> LoadAll()
    {
        lock (_fileLock)
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
                }
            }
            catch { }
            return new Dictionary<string, string>();
        }
    }

    private static void SaveAll(Dictionary<string, string> dict)
    {
        lock (_fileLock)
        {
            try
            {
                string? dir = Path.GetDirectoryName(SettingsFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                string json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFilePath, json);
            }
            catch { }
        }
    }

    public static string? Get(string key)
    {
        var dict = LoadAll();
        return dict.TryGetValue(key, out var val) ? val : null;
    }

    public static void Set(string key, string value)
    {
        lock (_fileLock)
        {
            var dict = LoadAll();
            dict[key] = value;
            SaveAll(dict);
        }
    }
}
