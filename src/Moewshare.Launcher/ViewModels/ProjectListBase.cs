using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Moewshare.Launcher.Logging;
using Moewshare.Launcher.Models;
using Moewshare.Launcher.Services;

namespace Moewshare.Launcher.ViewModels;

/// <summary>Общие действия над карточками проектов (Главная и «Мои проекты»).</summary>
public abstract partial class ProjectListBase(IProjectService projects, IEditorLauncherService launcher,
    IEditorInstallationService editors, IDialogService dialogs, ILoggingService log) : ObservableObject
{
    protected IProjectService Projects { get; } = projects;
    protected IEditorLauncherService Launcher { get; } = launcher;
    protected IEditorInstallationService Editors { get; } = editors;
    protected IDialogService Dialogs { get; } = dialogs;
    protected List<ProjectEntry> All { get; private set; } = [];

    public ObservableCollection<ProjectEntry> Items { get; } = [];
    protected abstract IEnumerable<ProjectEntry> Filter(IEnumerable<ProjectEntry> all);

    public async Task ReloadAsync()
    {
        try { All = await Projects.GetAllAsync(); }
        catch (Exception ex) { log.Error("Не удалось загрузить проекты", ex); All = []; }
        Rebuild();
    }

    protected void Rebuild()
    {
        Items.Clear();
        foreach (var p in Filter(All)) Items.Add(p);
    }

    [RelayCommand]
    async Task Open(ProjectEntry? p)
    {
        if (p is null) return;
        var r = await Launcher.OpenProjectAsync(p);
        if (!r.Ok) { await Dialogs.ShowMessageAsync("Не удалось открыть проект", r.Error!); return; }
        await Projects.TouchAsync(p);
        await ReloadAsync();
    }

    [RelayCommand]
    async Task ToggleFavorite(ProjectEntry? p)
    {
        if (p is null) return;
        await Projects.ToggleFavoriteAsync(p);
        await ReloadAsync();
    }

    [RelayCommand]
    async Task Rename(ProjectEntry? p)
    {
        if (p is null) return;
        var name = await Dialogs.PromptAsync("Переименование", "Новое название проекта:", p.Name);
        if (name is null) return;
        var r = await Projects.RenameAsync(p, name.Trim());
        if (!r.Ok) await Dialogs.ShowMessageAsync("Не удалось переименовать", r.Error!);
        await ReloadAsync();
    }

    [RelayCommand]
    async Task ChangeVersion(ProjectEntry? p)
    {
        if (p is null) return;
        var list = await Editors.ScanAsync();
        if (list.Count == 0) { await Dialogs.ShowMessageAsync("Версия редактора", "Установленных редакторов не найдено."); return; }
        var v = await Dialogs.PromptAsync("Версия редактора",
            $"Доступные версии: {string.Join(", ", list.Select(i => i.Version))}", p.EngineVersion ?? list[0].Version);
        if (v is null) return;
        if (!list.Any(i => i.Version == v.Trim())) { await Dialogs.ShowMessageAsync("Версия редактора", "Такой версии нет среди установленных."); return; }
        await Projects.SetEngineVersionAsync(p, v.Trim());
        await ReloadAsync();
    }

    [RelayCommand]
    void Reveal(ProjectEntry? p)
    {
        if (p is null) return;
        try
        {
            if (!Directory.Exists(p.Folder)) throw new DirectoryNotFoundException("Папка проекта не найдена.");
            ShellHelper.OpenFolder(p.Folder);
        }
        catch (Exception ex) { _ = Dialogs.ShowMessageAsync("Не удалось открыть папку", ex.Message); }
    }

    [RelayCommand]
    async Task Remove(ProjectEntry? p)
    {
        if (p is null) return;
        if (p.Status == ProjectStatus.Missing)
        {
            await Projects.RemoveFromListAsync(p);
            await ReloadAsync();
            return;
        }
        if (!await Dialogs.ConfirmAsync("Убрать из списка", $"Убрать «{p.Name}» из списка недавних? Файлы на диске останутся.", "Убрать")) return;
        await Projects.RemoveFromListAsync(p);
        await ReloadAsync();
    }

    [RelayCommand]
    async Task RemoveMissing(ProjectEntry? p)
    {
        if (p is null) return;
        await Projects.RemoveFromListAsync(p);
        await ReloadAsync();
    }

    [RelayCommand]
    async Task ClearAllMissing()
    {
        var missing = All.Where(p => p.Status == ProjectStatus.Missing).ToList();
        if (missing.Count == 0)
        {
            await Dialogs.ShowMessageAsync("Очистка проектов", "Все проекты в вашем списке доступны на диске!");
            return;
        }
        if (!await Dialogs.ConfirmAsync("Очистка недоступных проектов",
                $"Найдено {missing.Count} проект(ов), чьи папки были перемещены или удалены. Скрыть их из списка?", "Очистить")) return;

        foreach (var m in missing)
            await Projects.RemoveFromListAsync(m);

        await ReloadAsync();
    }

    [RelayCommand]
    async Task Delete(ProjectEntry? p)
    {
        if (p is null) return;
        if (p.Status == ProjectStatus.Missing)
        {
            await Projects.RemoveFromListAsync(p);
            await ReloadAsync();
            return;
        }
        if (!await Dialogs.ConfirmAsync("Удаление проекта",
                $"Папка «{p.Folder}» и все её файлы будут удалены с диска навсегда.\nВы уверены?", "Удалить проект")) return;
        var r = await Projects.DeleteFromDiskAsync(p);
        if (!r.Ok) await Dialogs.ShowMessageAsync("Не удалось удалить", r.Error!);
        await ReloadAsync();
    }
}
