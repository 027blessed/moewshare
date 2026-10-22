using Moewshare.Launcher.Configuration;
using Moewshare.Launcher.Models;
using Moewshare.Launcher.Repositories;

namespace Moewshare.Launcher.Services;

public interface ISettingsService
{
    AppSettings Current { get; }
    Task LoadAsync();
    Task SaveAsync();
    Task ResetAsync();
}

public sealed class SettingsService(string path) : ISettingsService
{
    readonly JsonFileStore<AppSettings> _store = new(path);
    readonly SemaphoreSlim _gate = new(1, 1);
    bool _loaded;
    public AppSettings Current { get; private set; } = new();

    public async Task LoadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_loaded) return;
            Current = await _store.LoadAsync();
            if (string.IsNullOrWhiteSpace(Current.ProjectsFolder)) Current.ProjectsFolder = AppPaths.DefaultProjects;
            if (string.IsNullOrWhiteSpace(Current.EnginesFolder)) Current.EnginesFolder = AppPaths.DefaultEditors;
            _loaded = true;
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync()
    {
        await _gate.WaitAsync();
        try { await _store.SaveAsync(Current); }
        finally { _gate.Release(); }
    }

    public async Task ResetAsync()
    {
        Current = new AppSettings();
        _loaded = true;
        await SaveAsync();
    }
}
