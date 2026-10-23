namespace Moewshare.Engine;

/// <summary>Режим оформления интерфейса лаунчера и редактора.</summary>
public enum UiThemeMode { System, Light, Dark }

/// <summary>
/// Общие пользовательские настройки внешнего вида, которые разделяют лаунчер и редактор.
/// Хранятся в маленьком текстовом файле, чтобы смена темы в лаунчере сразу применялась в редакторе.
/// </summary>
public static class UserPreferences
{
    /// <summary>Каталог пользовательских данных Moewshare (совпадает с каталогом лаунчера).</summary>
    public static string DataRoot
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

    public static string ThemeFile => Path.Combine(DataRoot, "theme.txt");

    public static UiThemeMode ParseTheme(string? value) =>
        Enum.TryParse<UiThemeMode>(value?.Trim(), ignoreCase: true, out var mode) ? mode : UiThemeMode.System;

    public static UiThemeMode LoadTheme()
    {
        try
        {
            if (File.Exists(ThemeFile)) return ParseTheme(File.ReadAllText(ThemeFile));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return UiThemeMode.System;
    }

    public static void SaveTheme(UiThemeMode mode)
    {
        try
        {
            Directory.CreateDirectory(DataRoot);
            File.WriteAllText(ThemeFile, mode.ToString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
