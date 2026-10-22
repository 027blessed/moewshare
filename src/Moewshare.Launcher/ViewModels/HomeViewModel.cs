using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Moewshare.Launcher.Logging;
using Moewshare.Launcher.Models;
using Moewshare.Launcher.Navigation;
using Moewshare.Launcher.Services;

namespace Moewshare.Launcher.ViewModels;

public sealed partial class HomeViewModel(IProjectService projects, IEditorLauncherService launcher,
    IEditorInstallationService editors, IDialogService dialogs, ILoggingService log,
    ITemplateService templates, INavigationService nav)
    : ProjectListBase(projects, launcher, editors, dialogs, log), IPage
{
    public IReadOnlyList<TemplateInfo> Templates { get; } = templates.GetAll();
    [ObservableProperty] bool _editorMissing;
    [ObservableProperty] bool _hasRecent;

    protected override IEnumerable<ProjectEntry> Filter(IEnumerable<ProjectEntry> all) =>
        all.OrderByDescending(p => p.LastModified).Take(4);

    public async Task InitializeAsync()
    {
        await ReloadAsync();
        HasRecent = Items.Count > 0;
        EditorMissing = (await Editors.ScanAsync()).Count == 0;
    }

    [RelayCommand]
    async Task OpenEditor()
    {
        var r = await Launcher.OpenEditorAsync();
        if (!r.Ok) await Dialogs.ShowMessageAsync("Не удалось открыть редактор", r.Error!);
    }

    [RelayCommand] Task NewProject() => nav.NavigateAsync<NewProjectViewModel>();
    [RelayCommand] Task StartTemplate(TemplateInfo? t) => nav.NavigateAsync<NewProjectViewModel>(vm => vm.Preselect(t));
    [RelayCommand] Task GoSettings() => nav.NavigateAsync<SettingsViewModel>();
}
