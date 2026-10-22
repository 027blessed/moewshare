using System.Text.Json;

namespace Moewshare.Editor.Configuration;

public sealed class EditorSettings
{
    // Общие
    public int AutoSaveIntervalMinutes { get; set; } = 5;
    public bool ShowGrid { get; set; } = true;
    public bool SnapToGrid { get; set; } = true;
    public double GridSize { get; set; } = 1.0;
    public string Theme { get; set; } = "PastelPink";

    // Рендер
    public int FpsLimit { get; set; } = 60;
    public bool AntiAliasing { get; set; } = true;

    // AI & MCP Сервер
    public bool McpEnabled { get; set; } = true;
    public int McpPort { get; set; } = 45124;
    public bool AutoStartMcp { get; set; } = true;
    public bool LogMcpRequests { get; set; } = true;
}

public static class EditorSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string SettingsFilePath
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var folder = OperatingSystem.IsMacOS()
                ? Path.Combine(home, "Library", "Application Support", "Moewshare")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Moewshare");

            Directory.CreateDirectory(folder);
            return Path.Combine(folder, "editor_settings.json");
        }
    }

    public static EditorSettings Load()
    {
        try
        {
            var path = SettingsFilePath;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<EditorSettings>(json);
                if (settings != null) return settings;
            }
        }
        catch
        {
            // fallback to defaults on read error
        }

        var defaultSettings = new EditorSettings();
        Save(defaultSettings);
        return defaultSettings;
    }

    public static void Save(EditorSettings settings)
    {
        try
        {
            var path = SettingsFilePath;
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch
        {
            // ignore save errors
        }
    }
}
