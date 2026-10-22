namespace Moewshare.Launcher.Configuration;

/// <summary>Все пути данных лаунчера. Данные не хранятся в каталоге установки.</summary>
public static class AppPaths
{
    public static string Root
    {
        get
        {
            var o = Environment.GetEnvironmentVariable("MOEWSHARE_DATA");
            if (!string.IsNullOrWhiteSpace(o)) return o;
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return OperatingSystem.IsMacOS()
                ? Path.Combine(home, "Library", "Application Support", "Moewshare")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Moewshare");
        }
    }
    public static string Logs => Path.Combine(Root, "Logs");
    public static string Temp => Path.Combine(Root, "Temp");
    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string RegistryFile => Path.Combine(Root, "projects.json");
    public static string DefaultEditors => Path.Combine(Root, "Editors");
    public static string DefaultProjects =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Moewshare Projects");
}
