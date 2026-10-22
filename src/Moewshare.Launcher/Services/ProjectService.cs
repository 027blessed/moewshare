using Moewshare.Launcher.Logging;
using Moewshare.Launcher.Models;
using Moewshare.Launcher.Repositories;

namespace Moewshare.Launcher.Services;

public interface IProjectService
{
    Task<List<ProjectEntry>> GetAllAsync();
    Task<OpResult<ProjectEntry>> CreateAsync(string name, string parentFolder, TemplateInfo template, string? engineVersion);
    Task<OpResult<ProjectEntry>> AddExistingAsync(string folder);
    Task<OpResult> RenameAsync(ProjectEntry entry, string newName);
    Task<OpResult> SetEngineVersionAsync(ProjectEntry entry, string version);
    Task RemoveFromListAsync(ProjectEntry entry);
    Task<OpResult> DeleteFromDiskAsync(ProjectEntry entry);
    Task ToggleFavoriteAsync(ProjectEntry entry);
    Task TouchAsync(ProjectEntry entry);
}

/// <summary>Формат проекта (project.json, сцены, шаблоны) принадлежит Moewshare.Engine; лаунчер лишь ведёт реестр.</summary>
public sealed class ProjectService(IProjectRegistryService registry, ILoggingService log) : IProjectService
{
    public const string ManifestFileName = EngineInfo.ProjectFile;
    static string ManifestPath(string folder) => Path.Combine(folder, ManifestFileName);

    public async Task<List<ProjectEntry>> GetAllAsync()
    {
        var all = await registry.GetAllAsync();
        await Task.WhenAll(all.Select(async p => p.Status = await ValidateAsync(p)));
        return all;
    }

    static async Task<ProjectStatus> ValidateAsync(ProjectEntry p)
    {
        if (!Directory.Exists(p.Folder)) return ProjectStatus.Missing;
        var m = await EngineJson.TryReadAsync<ProjectConfig>(ManifestPath(p.Folder));
        return m is null ? ProjectStatus.Corrupt : ProjectStatus.Ok;
    }

    public async Task<OpResult<ProjectEntry>> CreateAsync(string name, string parentFolder, TemplateInfo template, string? engineVersion)
    {
        var err = PathValidator.ValidateName(name) ?? PathValidator.ValidateFolder(parentFolder);
        if (err is not null) return OpResult<ProjectEntry>.Fail(err);

        var dir = Path.GetFullPath(Path.Combine(parentFolder, name));
        var existed = Directory.Exists(dir);
        if (existed && Directory.EnumerateFileSystemEntries(dir).Any())
            return OpResult<ProjectEntry>.Fail("Папка с таким названием уже существует и не пуста.");

        try
        {
            Directory.CreateDirectory(dir);
            var res = await ProjectScaffolder.CreateAsync(dir, name, template.Id, engineVersion);
            if (!res.Ok)
            {
                if (!existed) try { Directory.Delete(dir, true); } catch { }
                return OpResult<ProjectEntry>.Fail(res.Error!);
            }
            var cfg = res.Value!;
            var entry = new ProjectEntry
            {
                Id = cfg.Id, Name = name, Folder = dir, Kind = cfg.Kind,
                EngineVersion = cfg.EngineVersion, LastModified = DateTime.Now
            };
            await registry.UpsertAsync(entry);
            log.Info($"Создан проект {name} в {dir}");
            return OpResult<ProjectEntry>.Success(entry);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Error("Не удалось создать проект", ex);
            if (!existed) try { Directory.Delete(dir, true); } catch { }
            return OpResult<ProjectEntry>.Fail($"Не удалось создать проект: {ex.Message}");
        }
    }

    public async Task<OpResult<ProjectEntry>> AddExistingAsync(string folder)
    {
        folder = Path.GetFullPath(folder);
        var m = await EngineJson.TryReadAsync<ProjectConfig>(ManifestPath(folder));
        if (m is null)
            return OpResult<ProjectEntry>.Fail($"В папке нет корректного проекта Moewshare (файл {ManifestFileName} не найден или повреждён).");

        var cmp = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if ((await registry.GetAllAsync()).Any(p => string.Equals(p.Folder, folder, cmp)))
            return OpResult<ProjectEntry>.Fail("Этот проект уже есть в списке.");

        var e = new ProjectEntry
        {
            Name = m.Name, Folder = folder, Kind = m.Kind, EngineVersion = m.EngineVersion,
            LastModified = Directory.GetLastWriteTime(folder)
        };
        await registry.UpsertAsync(e);
        return OpResult<ProjectEntry>.Success(e);
    }

    public async Task<OpResult> RenameAsync(ProjectEntry entry, string newName)
    {
        var err = PathValidator.ValidateName(newName);
        if (err is not null) return OpResult.Fail(err);
        entry.Name = newName;
        await UpdateConfigAsync(entry, c => c.Name = newName);
        await registry.UpsertAsync(entry);
        return OpResult.Success;
    }

    public async Task<OpResult> SetEngineVersionAsync(ProjectEntry entry, string version)
    {
        entry.EngineVersion = version;
        await UpdateConfigAsync(entry, c => c.EngineVersion = version);
        await registry.UpsertAsync(entry);
        return OpResult.Success;
    }

    static async Task UpdateConfigAsync(ProjectEntry e, Action<ProjectConfig> change)
    {
        var path = ManifestPath(e.Folder);
        var c = await EngineJson.TryReadAsync<ProjectConfig>(path);
        if (c is null) return;
        change(c);
        await EngineJson.SaveAtomicAsync(path, c);
    }

    public Task RemoveFromListAsync(ProjectEntry entry) => registry.RemoveAsync(entry.Id);

    public async Task<OpResult> DeleteFromDiskAsync(ProjectEntry entry)
    {
        // Удаляем только каталоги, которые распознаны как проект Moewshare.
        if (!File.Exists(ManifestPath(entry.Folder)))
            return OpResult.Fail("Папка не похожа на проект Moewshare, поэтому удалять её небезопасно.");
        try
        {
            await Task.Run(() => Directory.Delete(entry.Folder, true));
            await registry.RemoveAsync(entry.Id);
            log.Info($"Проект удалён с диска: {entry.Folder}");
            return OpResult.Success;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Error("Не удалось удалить проект", ex);
            return OpResult.Fail($"Не удалось удалить папку: {ex.Message}");
        }
    }

    public Task ToggleFavoriteAsync(ProjectEntry entry)
    {
        entry.IsFavorite = !entry.IsFavorite;
        return registry.UpsertAsync(entry);
    }

    public Task TouchAsync(ProjectEntry entry)
    {
        entry.LastModified = DateTime.Now;
        return registry.UpsertAsync(entry);
    }
}
