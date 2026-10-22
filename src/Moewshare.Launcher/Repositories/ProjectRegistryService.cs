using Moewshare.Launcher.Models;

namespace Moewshare.Launcher.Repositories;

public sealed class RegistryFile
{
    public int Version { get; set; } = 1;
    public List<ProjectEntry> Projects { get; set; } = [];
}

public interface IProjectRegistryService
{
    Task<List<ProjectEntry>> GetAllAsync();
    Task UpsertAsync(ProjectEntry entry);
    Task RemoveAsync(Guid id);
}

public sealed class ProjectRegistryService(string path) : IProjectRegistryService
{
    readonly JsonFileStore<RegistryFile> _store = new(path);
    readonly SemaphoreSlim _gate = new(1, 1);
    RegistryFile? _cache;

    async Task<RegistryFile> LoadAsync() => _cache ??= await _store.LoadAsync();

    public async Task<List<ProjectEntry>> GetAllAsync()
    {
        await _gate.WaitAsync();
        try { return [.. (await LoadAsync()).Projects]; }
        finally { _gate.Release(); }
    }

    public async Task UpsertAsync(ProjectEntry entry)
    {
        await _gate.WaitAsync();
        try
        {
            var f = await LoadAsync();
            var i = f.Projects.FindIndex(p => p.Id == entry.Id);
            if (i >= 0) f.Projects[i] = entry; else f.Projects.Add(entry);
            await _store.SaveAsync(f);
        }
        finally { _gate.Release(); }
    }

    public async Task RemoveAsync(Guid id)
    {
        await _gate.WaitAsync();
        try
        {
            var f = await LoadAsync();
            f.Projects.RemoveAll(p => p.Id == id);
            await _store.SaveAsync(f);
        }
        finally { _gate.Release(); }
    }
}
