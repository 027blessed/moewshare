using System.Collections.ObjectModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Moewshare.Launcher.Configuration;
using Moewshare.Launcher.Logging;
using Moewshare.Launcher.Models;
using Moewshare.Launcher.Navigation;
using Moewshare.Launcher.Services;

namespace Moewshare.Launcher.ViewModels;

public sealed partial class SettingsViewModel(ISettingsService settings, IEditorInstallationService editors,
    IDialogService dialogs, ILoggingService log) : ObservableObject, IPage
{
    bool _loading;
    [ObservableProperty] string _projectsFolder = "";
    [ObservableProperty] string _enginesFolder = "";
    [ObservableProperty] bool _autoCheckUpdates;
    [ObservableProperty] string? _info;
    [ObservableProperty] int _themeIndex;   // 0 — как в системе, 1 — светлая, 2 — тёмная

    public bool IsThemeSystem { get => ThemeIndex == 0; set { if (value) ThemeIndex = 0; } }
    public bool IsThemeLight { get => ThemeIndex == 1; set { if (value) ThemeIndex = 1; } }
    public bool IsThemeDark { get => ThemeIndex == 2; set { if (value) ThemeIndex = 2; } }

    partial void OnThemeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsThemeSystem));
        OnPropertyChanged(nameof(IsThemeLight));
        OnPropertyChanged(nameof(IsThemeDark));
        if (_loading) return;
        var mode = value switch { 1 => UiThemeMode.Light, 2 => UiThemeMode.Dark, _ => UiThemeMode.System };
        ThemeService.Apply(mode);
    }

    public string ThemeHotkey => OperatingSystem.IsMacOS() ? "⇧⌘L" : "Ctrl+Shift+L";
    public string HotkeysText => OperatingSystem.IsMacOS()
        ? "⌘N — новый проект · ⌘O — добавить существующий · ⌘F — поиск · ⌘1…4 — вкладки · ⌘, — настройки · ⇧⌘L — тема · ⌘R — обновить"
        : "Ctrl+N — новый проект · Ctrl+O — добавить существующий · Ctrl+F — поиск · Ctrl+1…4 — вкладки · Ctrl+, — настройки · Ctrl+Shift+L — тема · F5 — обновить";
    public ObservableCollection<string> InstalledEditors { get; } = [];
    public string LauncherVersion { get; } = "Moewshare Launcher " +
        (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.1.0").Split('+')[0];

    public async Task InitializeAsync()
    {
        await settings.LoadAsync();
        _loading = true;
        ProjectsFolder = settings.Current.ProjectsFolder;
        EnginesFolder = settings.Current.EnginesFolder;
        AutoCheckUpdates = settings.Current.AutoCheckUpdates;
        ThemeIndex = ThemeService.Mode switch { UiThemeMode.Light => 1, UiThemeMode.Dark => 2, _ => 0 };
        _loading = false;
        await RefreshEditorsAsync();
    }

    async Task RefreshEditorsAsync()
    {
        InstalledEditors.Clear();
        foreach (var e in await editors.ScanAsync()) InstalledEditors.Add($"{e.Version} — {e.ExecutablePath}");
        if (InstalledEditors.Count == 0) InstalledEditors.Add("Редакторы не найдены в папке установок.");
    }

    async Task SaveAsync()
    {
        if (_loading) return;
        try
        {
            settings.Current.ProjectsFolder = ProjectsFolder;
            settings.Current.EnginesFolder = EnginesFolder;
            settings.Current.AutoCheckUpdates = AutoCheckUpdates;
            await settings.SaveAsync();
        }
        catch (Exception ex) { log.Error("Не удалось сохранить настройки", ex); Info = "Не удалось сохранить настройки: " + ex.Message; }
    }

    partial void OnProjectsFolderChanged(string value) => _ = SaveAsync();
    partial void OnEnginesFolderChanged(string value) { _ = SaveAsync(); if (!_loading) _ = RefreshEditorsAsync(); }
    partial void OnAutoCheckUpdatesChanged(bool value) => _ = SaveAsync();

    [RelayCommand] async Task BrowseProjects() { var f = await dialogs.PickFolderAsync("Папка проектов"); if (f is not null) ProjectsFolder = f; }
    [RelayCommand] async Task BrowseEngines() { var f = await dialogs.PickFolderAsync("Папка установок редактора"); if (f is not null) EnginesFolder = f; }

    [RelayCommand]
    void OpenLogs()
    {
        try { Directory.CreateDirectory(AppPaths.Logs); ShellHelper.OpenFolder(AppPaths.Logs); }
        catch (Exception ex) { Info = "Не удалось открыть папку журналов: " + ex.Message; }
    }

    [RelayCommand]
    async Task ClearTemp()
    {
        try
        {
            if (!Directory.Exists(AppPaths.Temp)) { Info = "Временных файлов нет."; return; }
            await Task.Run(() =>
            {
                foreach (var f in Directory.EnumerateFiles(AppPaths.Temp)) File.Delete(f);
                foreach (var d in Directory.EnumerateDirectories(AppPaths.Temp)) Directory.Delete(d, true);
            });
            Info = "Временные файлы удалены.";
        }
        catch (Exception ex) { Info = "Не удалось очистить временные файлы: " + ex.Message; }
    }

    [RelayCommand]
    async Task Reset()
    {
        if (!await dialogs.ConfirmAsync("Сброс настроек", "Вернуть все настройки к значениям по умолчанию? Проекты и файлы не будут затронуты.", "Сбросить")) return;
        await settings.ResetAsync();
        await InitializeAsync();
        Info = "Настройки сброшены.";
    }
}
