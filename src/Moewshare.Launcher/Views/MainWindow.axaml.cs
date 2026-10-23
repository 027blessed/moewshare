using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Moewshare.Launcher.Services;
using Moewshare.Launcher.ViewModels;

namespace Moewshare.Launcher.Views;

public partial class MainWindow : Window
{
    static bool IsMac => OperatingSystem.IsMacOS();
    static KeyModifiers Mod => IsMac ? KeyModifiers.Meta : KeyModifiers.Control;
    static string L(string key) => IsMac ? $"⌘{key}" : $"Ctrl+{key}";

    public MainWindow()
    {
        InitializeComponent();

        // Подсказки с горячими клавишами для текущей ОС
        ToolTip.SetTip(TabHome, $"Главная  ({L("1")})");
        ToolTip.SetTip(TabProjects, $"Мои проекты  ({L("2")})");
        ToolTip.SetTip(TabTemplates, $"Новый проект из шаблона  ({L("3")} или {L("N")})");
        ToolTip.SetTip(TabLibrary, $"Библиотека  ({L("4")})");
        ToolTip.SetTip(TabSettings, $"Настройки  ({L(",")})");
        ToolTip.SetTip(ThemeButton, $"Светлая / тёмная тема  ({(IsMac ? "⇧⌘L" : "Ctrl+Shift+L")})");

        ThemeService.Changed += UpdateThemeButton;
        ActualThemeVariantChanged += (_, _) => UpdateThemeButton();
        UpdateThemeButton();
    }

    void UpdateThemeButton() => ThemeButton.Content = ThemeService.IsDark ? "☀️" : "🌙";

    void OnToggleThemeClick(object? sender, RoutedEventArgs e) => ThemeService.Toggle();

    /// <summary>Горячие клавиши лаунчера (Ctrl на Windows, ⌘ на macOS).</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || DataContext is not MainViewModel vm) return;
        var mods = e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Meta);

        if (mods == (Mod | KeyModifiers.Shift) && e.Key == Key.L) { ThemeService.Toggle(); e.Handled = true; return; }

        if (mods == Mod)
        {
            switch (e.Key)
            {
                case Key.D1 or Key.NumPad1: _ = vm.GoHomeCommand.ExecuteAsync(null); e.Handled = true; return;
                case Key.D2 or Key.NumPad2: _ = vm.GoProjectsCommand.ExecuteAsync(null); e.Handled = true; return;
                case Key.D3 or Key.NumPad3 or Key.N: _ = vm.GoTemplatesCommand.ExecuteAsync(null); e.Handled = true; return;
                case Key.D4 or Key.NumPad4: _ = vm.GoLibraryCommand.ExecuteAsync(null); e.Handled = true; return;
                case Key.OemComma: _ = vm.GoSettingsCommand.ExecuteAsync(null); e.Handled = true; return;
                case Key.O: _ = vm.AddExistingProjectAsync(); e.Handled = true; return;
                case Key.F: _ = FocusSearchAsync(vm); e.Handled = true; return;
                case Key.R: _ = vm.RefreshAsync(); e.Handled = true; return;
            }
        }
        if (mods == KeyModifiers.None && e.Key == Key.F5) { _ = vm.RefreshAsync(); e.Handled = true; }
    }

    async Task FocusSearchAsync(MainViewModel vm)
    {
        if (vm.Nav.Current is not ProjectsViewModel) await vm.GoProjectsCommand.ExecuteAsync(null);
        Dispatcher.UIThread.Post(() =>
        {
            var box = this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.Name == "SearchBox");
            box?.Focus();
            box?.SelectAll();
        }, DispatcherPriority.Background);
    }

    protected override void OnClosed(EventArgs e)
    {
        ThemeService.Changed -= UpdateThemeButton;
        base.OnClosed(e);
    }
}
