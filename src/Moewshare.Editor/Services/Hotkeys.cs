using Avalonia.Input;

namespace Moewshare.Editor.Services;

/// <summary>
/// Горячие клавиши редактора с учётом платформы:
/// на Windows/Linux основной модификатор — Ctrl, на macOS — ⌘ Command.
/// Используется и для обработки нажатий, и для подписей в меню и подсказках.
/// </summary>
public static class Hk
{
    public static bool IsMac => OperatingSystem.IsMacOS();

    /// <summary>Основной модификатор команд: Ctrl (Windows) или ⌘ (macOS).</summary>
    public static KeyModifiers Mod => IsMac ? KeyModifiers.Meta : KeyModifiers.Control;

    const KeyModifiers Relevant = KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Meta;

    /// <summary>Чистые модификаторы нажатия (без служебных флагов).</summary>
    public static KeyModifiers Mods(KeyEventArgs e) => e.KeyModifiers & Relevant;

    /// <summary>Нажата ли клавиша ровно с указанными модификаторами.</summary>
    public static bool Is(KeyEventArgs e, Key key, KeyModifiers mods = KeyModifiers.None) =>
        e.Key == key && Mods(e) == mods;

    /// <summary>Mod + клавиша (Ctrl+X / ⌘X).</summary>
    public static bool Cmd(KeyEventArgs e, Key key) => Is(e, key, Mod);

    /// <summary>Mod + Shift + клавиша (Ctrl+Shift+X / ⇧⌘X).</summary>
    public static bool CmdShift(KeyEventArgs e, Key key) => Is(e, key, Mod | KeyModifiers.Shift);

    /// <summary>Клавиша без модификаторов (одиночные клавиши: Q, W, E, R, F...).</summary>
    public static bool Plain(KeyEventArgs e, Key key) => Is(e, key);

    /// <summary>
    /// «Удалить»: Delete на Windows; ⌫ (Backspace), ⌦ или ⌘⌫ на macOS.
    /// </summary>
    public static bool IsDelete(KeyEventArgs e)
    {
        var m = Mods(e);
        if (IsMac) return (e.Key is Key.Back or Key.Delete) && (m == KeyModifiers.None || m == KeyModifiers.Meta);
        return (e.Key == Key.Delete && m == KeyModifiers.None) || (e.Key == Key.Back && m == KeyModifiers.Control);
    }

    public static bool IsUndo(KeyEventArgs e) => Cmd(e, Key.Z);

    public static bool IsRedo(KeyEventArgs e) =>
        CmdShift(e, Key.Z) || (!IsMac && Is(e, Key.Y, KeyModifiers.Control));

    // ---- Жесты для подписей в меню (MenuItem.InputGesture) ----
    static KeyGesture G(Key key, KeyModifiers mods = KeyModifiers.None) => new(key, mods);

    public static KeyGesture Save => G(Key.S, Mod);
    public static KeyGesture Open => G(Key.O, Mod);
    public static KeyGesture Undo => G(Key.Z, Mod);
    public static KeyGesture Redo => IsMac ? G(Key.Z, Mod | KeyModifiers.Shift) : G(Key.Y, Mod);
    public static KeyGesture Cut => G(Key.X, Mod);
    public static KeyGesture Copy => G(Key.C, Mod);
    public static KeyGesture Paste => G(Key.V, Mod);
    public static KeyGesture Duplicate => G(Key.D, Mod);
    public static KeyGesture SelectAll => G(Key.A, Mod);
    public static KeyGesture Delete => IsMac ? G(Key.Back) : G(Key.Delete);
    public static KeyGesture Rename => G(Key.F2);
    public static KeyGesture Settings => G(Key.OemComma, Mod);
    public static KeyGesture AddObject => G(Key.A, Mod | KeyModifiers.Shift);
    public static KeyGesture ToggleTheme => G(Key.L, Mod | KeyModifiers.Shift);
    public static KeyGesture Help => G(Key.F1);
    public static KeyGesture Run => G(Key.F5);
    public static KeyGesture Stop => G(Key.F5, KeyModifiers.Shift);
    public static KeyGesture Build => G(Key.B, Mod);
    public static KeyGesture ToggleBottom => G(Key.J, Mod);
    public static KeyGesture ObjectGraph => G(Key.E, Mod | KeyModifiers.Shift);
    public static KeyGesture Group => G(Key.G, Mod);
    public static KeyGesture Ungroup => G(Key.G, Mod | KeyModifiers.Shift);
    public static KeyGesture Focus => G(Key.F);
    public static KeyGesture ToggleHide => G(Key.H);
    public static KeyGesture ModeSelect => G(Key.Q);
    public static KeyGesture ModeMove => G(Key.W);
    public static KeyGesture ModeRotate => G(Key.E);
    public static KeyGesture ModeScale => G(Key.R);
    public static KeyGesture Panel1 => G(Key.D1, Mod);
    public static KeyGesture Panel2 => G(Key.D2, Mod);
    public static KeyGesture Panel3 => G(Key.D3, Mod);
    public static KeyGesture Panel4 => G(Key.D4, Mod);
    public static KeyGesture Panel5 => G(Key.D5, Mod);
    public static KeyGesture Panel6 => G(Key.D6, Mod);
    public static KeyGesture Panel7 => G(Key.D7, Mod);

    /// <summary>Описание сочетания для справки — сразу для Windows и macOS.</summary>
    public sealed record Combo(Key Key, bool Cmd = false, bool Shift = false, bool Alt = false)
    {
        KeyGesture Gesture => new(Key,
            (Cmd ? KeyModifiers.Control : KeyModifiers.None) |
            (Shift ? KeyModifiers.Shift : KeyModifiers.None) |
            (Alt ? KeyModifiers.Alt : KeyModifiers.None));
        public string Win => WinLabel(Gesture);
        public string Mac => MacLabel(Gesture);
    }

    /// <summary>Подпись сочетания для текущей ОС: «Ctrl+S» или «⌘S».</summary>
    public static string Label(KeyGesture g) => IsMac ? MacLabel(g) : WinLabel(g);

    /// <summary>Короткая подпись для тултипов: «Сохранить (⌘S)».</summary>
    public static string Tip(string text, KeyGesture g) => $"{text}  ({Label(g)})";

    public static string WinLabel(KeyGesture g)
    {
        var parts = new List<string>();
        if (g.KeyModifiers.HasFlag(KeyModifiers.Control) || g.KeyModifiers.HasFlag(KeyModifiers.Meta)) parts.Add("Ctrl");
        if (g.KeyModifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (g.KeyModifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        parts.Add(KeyName(g.Key, mac: false));
        return string.Join("+", parts);
    }

    public static string MacLabel(KeyGesture g)
    {
        var s = "";
        if (g.KeyModifiers.HasFlag(KeyModifiers.Control) && !IsMacPrimary(g)) s += "⌃";
        if (g.KeyModifiers.HasFlag(KeyModifiers.Alt)) s += "⌥";
        if (g.KeyModifiers.HasFlag(KeyModifiers.Shift)) s += "⇧";
        if (IsMacPrimary(g)) s += "⌘";
        return s + KeyName(g.Key, mac: true);
    }

    // Жесты строятся с Mod текущей ОС; для macOS-подписи считаем и Ctrl, и Meta командной клавишей.
    static bool IsMacPrimary(KeyGesture g) =>
        g.KeyModifiers.HasFlag(KeyModifiers.Meta) || g.KeyModifiers.HasFlag(KeyModifiers.Control);

    public static string KeyName(Key key, bool mac) => key switch
    {
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemQuestion => "/",
        Key.OemPlus => "=",
        Key.OemMinus => "−",
        Key.Delete => mac ? "⌦" : "Delete",
        Key.Back => mac ? "⌫" : "Backspace",
        Key.Escape => "Esc",
        Key.Enter => mac ? "↩" : "Enter",
        Key.Space => mac ? "␣" : "Пробел",
        Key.Up => "↑",
        Key.Down => "↓",
        Key.Left => "←",
        Key.Right => "→",
        Key.Home => "Home",
        _ when key >= Key.D0 && key <= Key.D9 => ((int)key - (int)Key.D0).ToString(),
        _ => key.ToString()
    };
}
