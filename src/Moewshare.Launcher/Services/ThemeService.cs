using Avalonia;
using Avalonia.Styling;

namespace Moewshare.Launcher.Services;

/// <summary>
/// Светлая / тёмная тема лаунчера. Выбор сохраняется в общий файл (theme.txt),
/// поэтому редактор открывается в той же теме.
/// </summary>
public static class ThemeService
{
    public static UiThemeMode Mode { get; private set; } = UiThemeMode.System;
    public static event Action? Changed;

    public static bool IsDark => Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

    public static void LoadAndApply() => Apply(UserPreferences.LoadTheme(), persist: false);

    public static void Apply(UiThemeMode mode, bool persist = true)
    {
        Mode = mode;
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = mode switch
            {
                UiThemeMode.Light => ThemeVariant.Light,
                UiThemeMode.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default
            };
        }
        if (persist) UserPreferences.SaveTheme(mode);
        Changed?.Invoke();
    }

    public static void Toggle() => Apply(IsDark ? UiThemeMode.Light : UiThemeMode.Dark);
}
