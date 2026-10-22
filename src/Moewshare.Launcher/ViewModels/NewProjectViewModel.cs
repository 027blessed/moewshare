using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Moewshare.Launcher.Logging;
using Moewshare.Launcher.Models;
using Moewshare.Launcher.Navigation;
using Moewshare.Launcher.Services;

namespace Moewshare.Launcher.ViewModels;

public sealed partial class NewProjectViewModel(ITemplateService templates, IProjectService projects,
    ISettingsService settings, IEditorInstallationService editors, INavigationService nav,
    IDialogService dialogs, ILoggingService log) : ObservableObject, IPage
{
    public IReadOnlyList<TemplateInfo> Templates { get; } = templates.GetAll();
    public ObservableCollection<string> Versions { get; } = [];

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsStep0), nameof(IsStep1), nameof(IsStep2), nameof(CanBack), nameof(Summary), nameof(StepTitle))]
    int _step;
    [ObservableProperty] TemplateInfo? _selectedTemplate;
    [ObservableProperty] string _name = "";
    [ObservableProperty] string _parentFolder = "";
    [ObservableProperty] string? _selectedVersion;
    [ObservableProperty] string? _error;
    [ObservableProperty] bool _busy;

    public bool IsStep0 => Step == 0;
    public bool IsStep1 => Step == 1;
    public bool IsStep2 => Step == 2;
    public bool CanBack => Step > 0;
    public string StepTitle => Step switch { 0 => "1 из 3 · Выберите шаблон", 1 => "2 из 3 · Название и папка", _ => "3 из 3 · Проверка" };
    public string Summary =>
        $"Шаблон: {SelectedTemplate?.Name} ({SelectedTemplate?.Status})\nНазвание: {Name}\n" +
        $"Папка: {Path.Combine(ParentFolder ?? "", Name ?? "")}\n" +
        $"Редактор: {SelectedVersion ?? "не установлен — версия не будет привязана к проекту"}" +
        "\n\nБудут созданы project.json, сцена, ресурсы и файлы Web-запуска. Скрипты C# в версии 0.1 не исполняются.";

    public void Preselect(TemplateInfo? t) { if (t is not null) SelectedTemplate = t; }

    public async Task InitializeAsync()
    {
        await settings.LoadAsync();
        ParentFolder = settings.Current.ProjectsFolder;
        foreach (var e in await editors.ScanAsync()) Versions.Add(e.Version);
        SelectedVersion = Versions.FirstOrDefault();
        SelectedTemplate ??= Templates[0];
    }

    string? ValidateStep() => Step switch
    {
        0 => SelectedTemplate is null ? "Выберите шаблон." : null,
        1 => PathValidator.ValidateName(Name) ?? PathValidator.ValidateFolder(ParentFolder) ?? CheckTarget(),
        _ => null
    };

    string? CheckTarget()
    {
        var dir = Path.GetFullPath(Path.Combine(ParentFolder, Name));
        return Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any()
            ? "Папка с таким названием уже существует и не пуста." : null;
    }

    [RelayCommand]
    void Next()
    {
        Error = ValidateStep();
        if (Error is null && Step < 2) Step++;
    }

    [RelayCommand] void Back() { Error = null; if (Step > 0) Step--; }

    [RelayCommand]
    async Task Browse()
    {
        var f = await dialogs.PickFolderAsync("Папка для проекта");
        if (f is not null) ParentFolder = f;
    }

    [RelayCommand]
    async Task Create()
    {
        if (SelectedTemplate is null) return;
        Busy = true;
        try
        {
            var r = await projects.CreateAsync(Name, ParentFolder, SelectedTemplate, SelectedVersion);
            if (!r.Ok) { Error = r.Error; return; }
            await nav.NavigateAsync<ProjectsViewModel>();
        }
        catch (Exception ex) { log.Error("Ошибка мастера создания проекта", ex); Error = ex.Message; }
        finally { Busy = false; }
    }

    [RelayCommand] Task Cancel() => nav.NavigateAsync<HomeViewModel>();

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(Summary));
    partial void OnParentFolderChanged(string value) => OnPropertyChanged(nameof(Summary));
}
