using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Moewshare.Engine;

namespace Moewshare.Editor.Services;

/// <summary>
/// Переключение светлой / тёмной темы редактора. Выбор общий с лаунчером
/// (файл theme.txt в каталоге данных Moewshare).
/// </summary>
public static class ThemeManager
{
    public static UiThemeMode Mode { get; private set; } = UiThemeMode.System;

    /// <summary>Вызывается после смены темы (для перерисовки кастомных полотен).</summary>
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

    /// <summary>Быстрое переключение «светлая ↔ тёмная» (горячая клавиша).</summary>
    public static void Toggle() => Apply(IsDark ? UiThemeMode.Light : UiThemeMode.Dark);

    public static string Title(UiThemeMode mode) => mode switch
    {
        UiThemeMode.Light => "☀️ Светлая",
        UiThemeMode.Dark => "🌙 Тёмная",
        _ => "🖥 Как в системе"
    };
}

/// <summary>Доступ к цветам палитры из кода (для полотен, которые рисуются вручную).</summary>
public static class ThemeRes
{
    public static IBrush ResBrush(this Control control, string key, IBrush fallback)
    {
        if (control.TryFindResource(key, control.ActualThemeVariant, out var value) && value is IBrush brush)
            return brush;
        return fallback;
    }

    public static Color ResColor(this Control control, string key, Color fallback) =>
        control.ResBrush(key, new SolidColorBrush(fallback)) is ISolidColorBrush solid ? solid.Color : fallback;
}
