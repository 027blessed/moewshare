using Moewshare.Launcher.Logging;
using Moewshare.Launcher.Models;
using Moewshare.Launcher.Repositories;
using Moewshare.Launcher.Services;
using Xunit;

namespace Moewshare.Launcher.Tests;

public abstract class TempDirTest : IDisposable
{
    protected readonly string Dir = Path.Combine(Path.GetTempPath(), "mw-" + Guid.NewGuid().ToString("N"));
    protected TempDirTest() => Directory.CreateDirectory(Dir);
    public void Dispose() { try { Directory.Delete(Dir, true); } catch { } }
}

public class SettingsServiceTests : TempDirTest
{
    string P => Path.Combine(Dir, "settings.json");

    [Fact]
    public async Task Save_then_load_roundtrips()
    {
        var a = new SettingsService(P); await a.LoadAsync();
        a.Current.Language = "en"; await a.SaveAsync();
        var b = new SettingsService(P); await b.LoadAsync();
        Assert.Equal("en", b.Current.Language);
    }

    [Fact]
    public async Task Corrupt_file_falls_back_to_defaults()
    {
        await File.WriteAllTextAsync(P, "{ broken");
        var s = new SettingsService(P); await s.LoadAsync();
        Assert.Equal("ru", s.Current.Language);
    }

    [Fact]
    public async Task Corrupt_file_is_recovered_from_backup()
    {
        var a = new SettingsService(P); await a.LoadAsync();
        a.Current.Language = "en"; await a.SaveAsync();
        a.Current.Language = "de"; await a.SaveAsync();      // «en» уходит в .bak
        await File.WriteAllTextAsync(P, "garbage");
        var b = new SettingsService(P); await b.LoadAsync();
        Assert.Equal("en", b.Current.Language);
    }

    [Fact]
    public async Task Reset_restores_defaults()
    {
        var a = new SettingsService(P); await a.LoadAsync();
        a.Current.AutoCheckUpdates = false; await a.ResetAsync();
        Assert.True(a.Current.AutoCheckUpdates);
    }
}

public class ProjectServiceTests : TempDirTest
{
    readonly TemplateInfo _tpl = new TemplateService().Find("platformer2d")!;
    ProjectService Make() => new(new ProjectRegistryService(Path.Combine(Dir, "projects.json")), new NullLoggingService());

    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("CON")]
    [InlineData("name.")]
    public async Task Invalid_names_are_rejected(string name)
    {
        var r = await Make().CreateAsync(name, Dir, _tpl, null);
        Assert.False(r.Ok);
    }

    [Fact]
    public async Task Create_makes_real_files_and_registers_project()
    {
        var svc = Make();
        var r = await svc.CreateAsync("Game", Dir, _tpl, "0.1.0");
        Assert.True(r.Ok, r.Error);
        Assert.True(File.Exists(Path.Combine(Dir, "Game", ProjectService.ManifestFileName)));
        Assert.True(Directory.Exists(Path.Combine(Dir, "Game", "Scenes")));
        var all = await svc.GetAllAsync();
        Assert.Single(all);
        Assert.Equal(ProjectStatus.Ok, all[0].Status);
    }

    [Fact]
    public async Task Create_refuses_non_empty_existing_folder()
    {
        Directory.CreateDirectory(Path.Combine(Dir, "Game"));
        File.WriteAllText(Path.Combine(Dir, "Game", "keep.txt"), "x");
        var r = await Make().CreateAsync("Game", Dir, _tpl, null);
        Assert.False(r.Ok);
        Assert.True(File.Exists(Path.Combine(Dir, "Game", "keep.txt")));
    }

    [Fact]
    public async Task Missing_folder_is_reported()
    {
        var svc = Make();
        var r = await svc.CreateAsync("Game", Dir, _tpl, null);
        Directory.Delete(r.Value!.Folder, true);
        Assert.Equal(ProjectStatus.Missing, (await svc.GetAllAsync())[0].Status);
    }

    [Fact]
    public async Task Delete_refuses_folders_that_are_not_projects()
    {
        var folder = Path.Combine(Dir, "Other"); Directory.CreateDirectory(folder);
        var r = await Make().DeleteFromDiskAsync(new ProjectEntry { Name = "x", Folder = folder });
        Assert.False(r.Ok);
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public async Task Rename_and_favorite_persist()
    {
        var svc = Make();
        var e = (await svc.CreateAsync("Game", Dir, _tpl, null)).Value!;
        Assert.True((await svc.RenameAsync(e, "Renamed")).Ok);
        await svc.ToggleFavoriteAsync(e);
        var again = (await Make().GetAllAsync()).Single();
        Assert.Equal("Renamed", again.Name);
        Assert.True(again.IsFavorite);
    }

    [Fact]
    public async Task Add_existing_requires_manifest()
    {
        var r = await Make().AddExistingAsync(Dir);
        Assert.False(r.Ok);
    }
}
