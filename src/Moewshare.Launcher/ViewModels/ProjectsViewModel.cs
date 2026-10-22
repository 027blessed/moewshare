using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Moewshare.Launcher.Logging;
using Moewshare.Launcher.Models;
using Moewshare.Launcher.Navigation;
using Moewshare.Launcher.Services;

namespace Moewshare.Launcher.ViewModels;

public sealed partial class ProjectsViewModel(IProjectService projects, IEditorLauncherService launcher,
    IEditorInstallationService editors, IDialogService dialogs, ILoggingService log,
    INavigationService nav, ISettingsService settings)
    : ProjectListBase(projects, launcher, editors, dialogs, log), IPage
{
    [ObservableProperty] string _searchText = "";
    [ObservableProperty] int _sortIndex;     // 0 — по дате, 1 — по названию
    [ObservableProperty] int _kindIndex;     // 0 — все, 1 — 2D, 2 — 3D
    [ObservableProperty] bool _isEmpty;
    [ObservableProperty] bool _hideMissing;
    [ObservableProperty] int _missingCount;

    partial void OnSearchTextChanged(string value) => Refresh();
    partial void OnSortIndexChanged(int value) => Refresh();
    partial void OnKindIndexChanged(int value) => Refresh();
    partial void OnHideMissingChanged(bool value) => Refresh();
    void Refresh()
    {
        MissingCount = All.Count(p => p.Status == ProjectStatus.Missing);
        Rebuild();
        IsEmpty = Items.Count == 0;
    }

    protected override IEnumerable<ProjectEntry> Filter(IEnumerable<ProjectEntry> all)
    {
        var q = all.Where(p =>
            (!HideMissing || p.Status != ProjectStatus.Missing) &&
            (KindIndex == 0 || p.Kind == (KindIndex == 1 ? ProjectKind.TwoD : ProjectKind.ThreeD)) &&
            (string.IsNullOrWhiteSpace(SearchText) || p.Name.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase)));
        return (SortIndex == 1
            ? q.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            : q.OrderByDescending(p => p.LastModified))
            .OrderByDescending(p => p.IsFavorite);   // избранное — сверху (порядок внутри групп сохраняется)
    }

    public async Task InitializeAsync()
    {
        await ReloadAsync();
        Refresh();
    }

    [RelayCommand] Task NewProject() => nav.NavigateAsync<NewProjectViewModel>();

    [RelayCommand]
    async Task AddExisting()
    {
        await settings.LoadAsync();
        var folder = await Dialogs.PickFolderAsync("Выберите папку проекта Moewshare");
        if (folder is null) return;
        var r = await Projects.AddExistingAsync(folder);
        if (!r.Ok) await Dialogs.ShowMessageAsync("Не удалось добавить проект", r.Error!);
        await InitializeAsync();
    }
}
