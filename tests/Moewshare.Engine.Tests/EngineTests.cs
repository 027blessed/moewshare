using Moewshare.Engine;
using Xunit;

namespace Moewshare.Engine.Tests;

public class EngineTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "mwe-" + Guid.NewGuid().ToString("N"));
    public EngineTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Theory]
    [InlineData("empty")] [InlineData("empty3d")] [InlineData("platformer2d")] [InlineData("topdown")] [InlineData("adventure3d")] [InlineData("coin_collector")] [InlineData("clicker")]
    public async Task Every_template_creates_a_valid_project(string template)
    {
        var folder = Path.Combine(_dir, template);
        var r = await ProjectScaffolder.CreateAsync(folder, "Game", template);
        Assert.True(r.Ok, r.Error);
        foreach (var f in new[] { "project.json", "index.html", "README.md", "Runtime/moewshare-runtime.js", "Scenes/Main.scene.json" })
            Assert.True(File.Exists(Path.Combine(folder, f)), f);
        foreach (var d in new[] { "Assets", "Scripts", "Build" }) Assert.True(Directory.Exists(Path.Combine(folder, d)), d);
        Assert.Empty(await ProjectValidator.ValidateAsync(folder));
    }

    [Fact]
    public async Task Scaffolder_does_not_overwrite_existing_project()
    {
        var folder = Path.Combine(_dir, "p");
        Assert.True((await ProjectScaffolder.CreateAsync(folder, "A", "empty")).Ok);
        Assert.False((await ProjectScaffolder.CreateAsync(folder, "B", "empty")).Ok);
        Assert.Equal("A", (await EngineJson.TryReadAsync<ProjectConfig>(Path.Combine(folder, "project.json")))!.Name);
    }

    [Fact]
    public async Task Unknown_template_is_rejected()
        => Assert.False((await ProjectScaffolder.CreateAsync(Path.Combine(_dir, "x"), "A", "nope")).Ok);

    [Fact]
    public async Task Scene_roundtrips_through_json()
    {
        var path = Path.Combine(_dir, "s.json");
        var scene = TemplateCatalog.Find("platformer2d")!.Build();
        await EngineJson.SaveAtomicAsync(path, scene);
        var back = (await EngineJson.TryReadAsync<SceneDocument>(path))!;
        Assert.Equal(scene.Objects.Count, back.Objects.Count);
        Assert.Equal(scene.Objects[0].Position.X, back.Objects[0].Position.X);
        Assert.Contains("\"objects\"", await File.ReadAllTextAsync(path)); // JS-рантайм читает camelCase
    }

    [Fact]
    public async Task Validator_reports_missing_scene_and_bad_objects()
    {
        var folder = Path.Combine(_dir, "v");
        await ProjectScaffolder.CreateAsync(folder, "A", "empty");
        var scenePath = Path.Combine(folder, "Scenes", "Main.scene.json");
        var scene = (await EngineJson.TryReadAsync<SceneDocument>(scenePath))!;
        scene.Objects.Add(new SceneObject { Id = scene.Objects[0].Id, Type = "dragon" });
        await EngineJson.SaveAtomicAsync(scenePath, scene);
        var errors = await ProjectValidator.ValidateAsync(folder);
        Assert.Contains(errors, e => e.Contains("Повторяющийся"));
        Assert.Contains(errors, e => e.Contains("dragon"));

        File.Delete(scenePath);
        Assert.Contains(await ProjectValidator.ValidateAsync(folder), e => e.Contains("не найдена"));
    }

    [Fact]
    public async Task Start_scene_outside_project_is_rejected()
    {
        var folder = Path.Combine(_dir, "o");
        await ProjectScaffolder.CreateAsync(folder, "A", "empty");
        var cfgPath = Path.Combine(folder, "project.json");
        var cfg = (await EngineJson.TryReadAsync<ProjectConfig>(cfgPath))!;
        cfg.StartScene = "../../etc/passwd";
        await EngineJson.SaveAtomicAsync(cfgPath, cfg);
        Assert.NotEmpty(await ProjectValidator.ValidateAsync(folder));
    }

    [Fact]
    public async Task Server_serves_project_files_and_blocks_traversal()
    {
        var folder = Path.Combine(_dir, "srv");
        await ProjectScaffolder.CreateAsync(folder, "A", "platformer2d");
        File.WriteAllText(Path.Combine(_dir, "secret.txt"), "secret");
        using var server = new LocalGameServer(folder);
        server.Start();
        using var http = new HttpClient();
        var index = await http.GetAsync(server.Url);
        Assert.Equal(System.Net.HttpStatusCode.OK, index.StatusCode);
        Assert.Contains("<canvas", await index.Content.ReadAsStringAsync());
        Assert.Equal(System.Net.HttpStatusCode.OK, (await http.GetAsync(server.Url + "Scenes/Main.scene.json")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await http.GetAsync(server.Url + "nope.js")).StatusCode);
        var trav = await http.GetAsync(server.Url + "..%2fsecret.txt");
        Assert.NotEqual(System.Net.HttpStatusCode.OK, trav.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.MethodNotAllowed, (await http.PostAsync(server.Url, new StringContent(""))).StatusCode);
    }

    [Fact]
    public async Task Session_start_stop_and_double_start()
    {
        var folder = Path.Combine(_dir, "ses");
        await ProjectScaffolder.CreateAsync(folder, "A", "empty");
        using var s = new GameSession();
        var r = await s.StartAsync(folder, openBrowser: false);
        Assert.True(r.Ok, r.Error);
        Assert.True(s.IsRunning);
        Assert.False((await s.StartAsync(folder, false)).Ok);
        s.Stop();
        Assert.False(s.IsRunning);
    }

    [Fact]
    public async Task Session_refuses_broken_project()
    {
        using var s = new GameSession();
        Assert.False((await s.StartAsync(Path.Combine(_dir, "missing"), false)).Ok);
    }

    [Fact]
    public async Task Export_produces_deployable_folder()
    {
        var folder = Path.Combine(_dir, "exp");
        await ProjectScaffolder.CreateAsync(folder, "A", "topdown");
        File.WriteAllText(Path.Combine(folder, "Assets", "a.txt"), "x");
        var r = await WebExporter.ExportAsync(folder);
        Assert.True(r.Ok, r.Error);
        foreach (var f in new[] { "index.html", "project.json", "Runtime/moewshare-runtime.js", "Scenes/Main.scene.json", "Assets/a.txt" })
            Assert.True(File.Exists(Path.Combine(r.Value!, f)), f);
    }

    [Fact]
    public async Task Export_stops_on_invalid_project()
        => Assert.False((await WebExporter.ExportAsync(Path.Combine(_dir, "none"))).Ok);

    [Fact]
    public async Task Cylinder_and_plane_are_valid_object_types()
    {
        var folder = Path.Combine(_dir, "cyl_plane");
        await ProjectScaffolder.CreateAsync(folder, "ShapesGame", "empty");
        var scenePath = Path.Combine(folder, "Scenes", "Main.scene.json");
        var scene = (await EngineJson.TryReadAsync<SceneDocument>(scenePath))!;
        scene.Objects.Add(new SceneObject { Name = "Cyl", Type = ObjectTypes.Cylinder, Position = new(0, 2, 0) });
        scene.Objects.Add(new SceneObject { Name = "Pln", Type = ObjectTypes.Plane, Position = new(0, 0, 0), Texture = "Assets/grass.png" });
        await EngineJson.SaveAtomicAsync(scenePath, scene);

        var errors = await ProjectValidator.ValidateAsync(folder);
        Assert.Empty(errors);

        var loaded = await EngineJson.TryReadAsync<SceneDocument>(scenePath);
        Assert.NotNull(loaded);
        Assert.Equal("Assets/grass.png", loaded.Objects.First(o => o.Name == "Pln").Texture);
    }

    [Fact]
    public async Task UiCanvas_and_elements_are_valid_and_parentable()
    {
        var folder = Path.Combine(_dir, "ui_canvas_test");
        await ProjectScaffolder.CreateAsync(folder, "UiGame", "empty");
        var scenePath = Path.Combine(folder, "Scenes", "Main.scene.json");
        var scene = (await EngineJson.TryReadAsync<SceneDocument>(scenePath))!;

        var canvas = new SceneObject
        {
            Type = ObjectTypes.Canvas,
            Name = "MainCanvas",
            UiDevicePreset = DevicePresets.MobilePortrait
        };
        var btn = new SceneObject
        {
            Type = ObjectTypes.UiButton,
            Name = "JumpBtn",
            ParentId = canvas.Id,
            UiText = "Прыжок",
            UiAction = "jump",
            UiFontSize = 18,
            Color = "#F7CDD5"
        };
        var txt = new SceneObject
        {
            Type = ObjectTypes.UiText,
            Name = "ScoreLabel",
            ParentId = canvas.Id,
            UiText = "Очки: 100",
            UiFontSize = 24
        };
        scene.Objects.Add(canvas);
        scene.Objects.Add(btn);
        scene.Objects.Add(txt);

        await EngineJson.SaveAtomicAsync(scenePath, scene);
        var errors = await ProjectValidator.ValidateAsync(folder);
        Assert.Empty(errors);

        var loaded = await EngineJson.TryReadAsync<SceneDocument>(scenePath);
        Assert.NotNull(loaded);
        var loadedBtn = loaded.Objects.First(o => o.Name == "JumpBtn");
        Assert.Equal(canvas.Id, loadedBtn.ParentId);
        Assert.Equal("jump", loadedBtn.UiAction);
        Assert.Equal("Прыжок", loadedBtn.UiText);
    }

    [Fact]
    public async Task Model3D_type_and_model_path_are_valid()
    {
        var folder = Path.Combine(_dir, "model_test");
        await ProjectScaffolder.CreateAsync(folder, "ModelGame", "empty");
        var scenePath = Path.Combine(folder, "Scenes", "Main.scene.json");
        var scene = (await EngineJson.TryReadAsync<SceneDocument>(scenePath))!;
        scene.Objects.Add(new SceneObject
        {
            Name = "HeroMesh",
            Type = ObjectTypes.Model,
            ModelPath = "Assets/Models/hero.obj",
            Position = new(0, 0, 0),
            Scale = new(1, 1, 1)
        });
        await EngineJson.SaveAtomicAsync(scenePath, scene);

        var errors = await ProjectValidator.ValidateAsync(folder);
        Assert.Empty(errors);

        var loaded = await EngineJson.TryReadAsync<SceneDocument>(scenePath);
        Assert.NotNull(loaded);
        var hero = loaded.Objects.First(o => o.Name == "HeroMesh");
        Assert.Equal(ObjectTypes.Model, hero.Type);
        Assert.Equal("Assets/Models/hero.obj", hero.ModelPath);
    }
}
