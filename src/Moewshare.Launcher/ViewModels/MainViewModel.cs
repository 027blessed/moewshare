using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Moewshare.Launcher.Logging;
using Moewshare.Launcher.Navigation;
using Moewshare.Launcher.Services;

namespace Moewshare.Launcher.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    readonly INavigationService _nav;
    readonly ISettingsService _settings;
    readonly IUpdateService _updates;
    readonly ILoggingService _log;

    public MainViewModel(INavigationService nav, ISettingsService settings, IUpdateService updates, ILoggingService log)
    {
        _nav = nav; _settings = settings; _updates = updates; _log = log;
        if (nav is INotifyPropertyChanged n)
            n.PropertyChanged += (_, _) => ActiveTab = nav.Current switch
            {
                HomeViewModel => "Home", ProjectsViewModel => "Projects", NewProjectViewModel => "Templates",
                LibraryViewModel => "Library", SettingsViewModel => "Settings", _ => ""
            };
    }

    public INavigationService Nav => _nav;
    [ObservableProperty] string _activeTab = "Home";
    [ObservableProperty] bool _updateAvailable;
    [ObservableProperty] string? _updateText;

    public async Task InitializeAsync()
    {
        await _nav.NavigateAsync<HomeViewModel>();
        try
        {
            await _settings.LoadAsync();
            if (!_settings.Current.AutoCheckUpdates) return;
            var r = await _updates.CheckAsync();
            UpdateAvailable = r.Available;
            UpdateText = r.Available ? $"Доступно обновление {r.Version}" : null;
        }
        catch (Exception ex) { _log.Error("Проверка обновлений не удалась", ex); }
    }

    [RelayCommand] Task GoHome() => _nav.NavigateAsync<HomeViewModel>();
    [RelayCommand] Task GoProjects() => _nav.NavigateAsync<ProjectsViewModel>();
    [RelayCommand] Task GoTemplates() => _nav.NavigateAsync<NewProjectViewModel>();
    [RelayCommand] Task GoLibrary() => _nav.NavigateAsync<LibraryViewModel>();
    [RelayCommand] Task GoSettings() => _nav.NavigateAsync<SettingsViewModel>();

    /// <summary>Ctrl+O / ⌘O — добавить существующий проект.</summary>
    public async Task AddExistingProjectAsync()
    {
        if (_nav.Current is not ProjectsViewModel) await _nav.NavigateAsync<ProjectsViewModel>();
        if (_nav.Current is ProjectsViewModel p) await p.AddExistingCommand.ExecuteAsync(null);
    }

    /// <summary>F5 / Ctrl+R / ⌘R — обновить текущую страницу.</summary>
    public async Task RefreshAsync()
    {
        if (_nav.Current is IPage page) await page.InitializeAsync();
    }
}
