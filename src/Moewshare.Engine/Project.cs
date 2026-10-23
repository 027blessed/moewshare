using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Reflection;

namespace Moewshare.Engine;

public static class EngineInfo { public const string Version = "0.1.0"; public const string ProjectFile = "project.json"; }

public static class RuntimeFiles
{
    public static string ReadRuntimeJs()
    {
        using var s = typeof(RuntimeFiles).Assembly.GetManifestResourceStream("moewshare-runtime.js")
                      ?? throw new InvalidOperationException("Встроенный рантайм не найден в сборке движка.");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    public static string IndexHtml(string name) => $$"""
        <!doctype html>
        <html lang="ru"><head><meta charset="utf-8"><title>{{WebUtility.HtmlEncode(name)}}</title>
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <style>html,body{margin:0;height:100%;overflow:hidden;background:#FFF4F3;font-family:system-ui,sans-serif}
        #game{width:100%;height:100%;display:block;outline:none}
        #msg{position:fixed;left:12px;right:12px;bottom:12px;color:#8b1e2d;background:#fff;border:1px solid #f1d9db;border-radius:12px;padding:10px 14px;display:none;white-space:pre-wrap}</style></head>
        <body><canvas id="game"></canvas><div id="msg"></div>
        <script src="Runtime/moewshare-runtime.js"></script>
        <script>Moewshare.start({ canvas: 'game', project: 'project.json' });</script>
        </body></html>
        """;

    /// <summary>Обновляет рантайм проекта до версии движка; index.html создаётся, только если его нет.</summary>
    public static async Task EnsureAsync(string folder, ProjectConfig cfg)
    {
        var dir = Path.Combine(folder, "Runtime");
        Directory.CreateDirectory(dir);
        var js = ReadRuntimeJs();
        var target = Path.Combine(dir, "moewshare-runtime.js");
        if (!File.Exists(target) || await File.ReadAllTextAsync(target) != js) await File.WriteAllTextAsync(target, js);
        var index = Path.Combine(folder, "index.html");
        if (!File.Exists(index)) await File.WriteAllTextAsync(index, IndexHtml(cfg.Name));
    }
}

public static class ProjectScaffolder
{
    public static async Task<EngineResult<ProjectConfig>> CreateAsync(string folder, string name, string templateId, string? engineVersion = null)
    {
        var t = TemplateCatalog.Find(templateId);
        if (t is null) return EngineResult<ProjectConfig>.Fail($"Неизвестный шаблон: {templateId}");
        folder = Path.GetFullPath(folder);
        if (File.Exists(Path.Combine(folder, EngineInfo.ProjectFile)))
            return EngineResult<ProjectConfig>.Fail("В этой папке уже есть проект Moewshare — он не будет перезаписан.");

        foreach (var d in new[] { "Scenes", "Assets", "Scripts", "Build", "Runtime" })
            Directory.CreateDirectory(Path.Combine(folder, d));

        var cfg = new ProjectConfig { Name = name, Kind = t.Kind, EngineVersion = engineVersion ?? EngineInfo.Version, TemplateId = t.Id };
        var scene = t.Build();
        await EngineJson.SaveAtomicAsync(Path.Combine(folder, cfg.StartScene), scene);
        await RuntimeFiles.EnsureAsync(folder, cfg);
        await File.WriteAllTextAsync(Path.Combine(folder, "README.md"),
            $"# {name}\n\nПроект Moewshare ({t.Name}).\n\n- `project.json` — конфигурация\n- `Scenes/` — сцены\n- `Assets/` — ресурсы\n- `Scripts/` — пока не исполняются (C#-скрипты запланированы)\n- `Build/web/` — результат экспорта\n- `index.html`, `Runtime/` — Web-запуск (нужен HTTP-сервер, его поднимает редактор)\n");
        await File.WriteAllTextAsync(Path.Combine(folder, "Scripts", "README.txt"),
            "Здесь будут C#-скрипты. В версии 0.1 они НЕ исполняются.\n");
        await EngineJson.SaveAtomicAsync(Path.Combine(folder, EngineInfo.ProjectFile), cfg); // последним: проект «появляется» только целиком
        return EngineResult<ProjectConfig>.Success(cfg);
    }
}

public static class ProjectValidator
{
    public static string? ResolveInside(string folder, string relative)
    {
        var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relative));
        var cmp = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return full.StartsWith(root, cmp) ? full : null;
    }

    public static async Task<List<string>> ValidateAsync(string folder)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) { errors.Add("Папка проекта не найдена."); return errors; }
        var cfg = await EngineJson.TryReadAsync<ProjectConfig>(Path.Combine(folder, EngineInfo.ProjectFile));
        if (cfg is null) { errors.Add("project.json отсутствует или повреждён."); return errors; }

        var scenePath = string.IsNullOrWhiteSpace(cfg.StartScene) ? null : ResolveInside(folder, cfg.StartScene);
        if (scenePath is null) { errors.Add("Стартовая сцена не задана или указывает за пределы проекта."); return errors; }
        var scene = await EngineJson.TryReadAsync<SceneDocument>(scenePath);
        if (scene is null) { errors.Add($"Стартовая сцена «{cfg.StartScene}» не найдена или повреждена."); return errors; }

        foreach (var g in scene.Objects.GroupBy(o => o.Id).Where(g => g.Count() > 1))
            errors.Add($"Повторяющийся идентификатор объекта: {g.Key}.");
        foreach (var o in scene.Objects)
        {
            if (!ObjectTypes.All.Contains(o.Type)) errors.Add($"Объект «{o.Name}»: неизвестный тип «{o.Type}».");
            if (!Behaviors.All.Contains(o.Behavior)) errors.Add($"Объект «{o.Name}»: неизвестное поведение «{o.Behavior}».");
        }
        if (!CameraModes.All.Contains(scene.Camera.Mode)) errors.Add($"Неизвестный режим камеры «{scene.Camera.Mode}».");
        return errors;
    }

    /// <summary>Обновляет рантайм и проверяет проект. Возвращает конфигурацию или текст ошибок.</summary>
    public static async Task<EngineResult<ProjectConfig>> PrepareAsync(string folder)
    {
        var errors = await ValidateAsync(folder);
        if (errors.Count > 0) return EngineResult<ProjectConfig>.Fail(string.Join("\n", errors));
        var cfg = (await EngineJson.TryReadAsync<ProjectConfig>(Path.Combine(folder, EngineInfo.ProjectFile)))!;
        try { await RuntimeFiles.EnsureAsync(folder, cfg); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return EngineResult<ProjectConfig>.Fail($"Не удалось подготовить файлы запуска: {ex.Message}"); }
        return EngineResult<ProjectConfig>.Success(cfg);
    }
}

/// <summary>Локальный HTTP-сервер только на 127.0.0.1: GET/HEAD, только файлы внутри папки проекта.</summary>
public sealed class LocalGameServer : IDisposable
{
    readonly HttpListener _listener = new();
    readonly string _root, _host;
    readonly CancellationTokenSource _cts = new();
    public string Url { get; }

    public LocalGameServer(string root)
    {
        _root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        _host = $"127.0.0.1:{port}";
        Url = $"http://{_host}/";
        _listener.Prefixes.Add(Url);
    }

    public void Start() { _listener.Start(); _ = Task.Run(LoopAsync); }

    async Task LoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); } catch { break; }
            _ = Task.Run(() => Handle(ctx));
        }
    }

    static string Mime(string f) => Path.GetExtension(f).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8", ".js" => "text/javascript; charset=utf-8", ".json" => "application/json",
        ".css" => "text/css", ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".svg" => "image/svg+xml",
        ".glb" => "model/gltf-binary", ".gltf" => "model/gltf+json", ".obj" => "model/obj", ".stl" => "model/stl", ".fbx" => "application/octet-stream", ".mp3" => "audio/mpeg", ".ogg" => "audio/ogg",
        ".wav" => "audio/wav", ".txt" => "text/plain; charset=utf-8", _ => "application/octet-stream"
    };

    void Handle(HttpListenerContext ctx)
    {
        var res = ctx.Response;
        try
        {
            if (ctx.Request.UserHostName != _host) { res.StatusCode = 400; return; }       // защита от DNS-rebinding
            if (ctx.Request.HttpMethod is not ("GET" or "HEAD")) { res.StatusCode = 405; return; }
            var rel = Uri.UnescapeDataString(ctx.Request.Url!.AbsolutePath).TrimStart('/');
            if (rel.Length == 0) rel = "index.html";
            var full = ProjectValidator.ResolveInside(_root, rel);
            if (full is null) { res.StatusCode = 403; return; }
            if (!File.Exists(full)) { res.StatusCode = 404; return; }
            var bytes = File.ReadAllBytes(full);
            res.ContentType = Mime(full);
            res.Headers["Cache-Control"] = "no-store";
            res.ContentLength64 = bytes.Length;
            if (ctx.Request.HttpMethod == "GET") res.OutputStream.Write(bytes);
        }
        catch { try { res.StatusCode = 500; } catch { } }
        finally { try { res.Close(); } catch { } }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); _listener.Close(); } catch { }
        _cts.Dispose();
    }
}

public sealed class GameSession : IDisposable
{
    LocalGameServer? _server;
    public bool IsRunning => _server is not null;
    public string? Url => _server?.Url;
    public string? Warning { get; private set; }

    public async Task<EngineResult<string>> StartAsync(string folder, bool openBrowser = true)
    {
        if (IsRunning) return EngineResult<string>.Fail("Игра уже запущена.");
        var prep = await ProjectValidator.PrepareAsync(folder);
        if (!prep.Ok) return EngineResult<string>.Fail(prep.Error!);
        try { var s = new LocalGameServer(folder); s.Start(); _server = s; }
        catch (Exception ex) when (ex is HttpListenerException or IOException or SocketException)
        { return EngineResult<string>.Fail($"Не удалось запустить локальный сервер: {ex.Message}"); }

        Warning = null;
        if (openBrowser)
        {
            try { Process.Start(new ProcessStartInfo(Url!) { UseShellExecute = true })?.Dispose(); }
            catch (Exception ex) { Warning = $"Не удалось открыть браузер ({ex.Message}). Откройте {Url} вручную."; }
        }
        return EngineResult<string>.Success(Url!);
    }

    public void Stop() { _server?.Dispose(); _server = null; }
    public void Dispose() => Stop();
}

public static class WebExporter
{
    public static async Task<EngineResult<string>> ExportAsync(string folder)
    {
        var prep = await ProjectValidator.PrepareAsync(folder);
        if (!prep.Ok) return EngineResult<string>.Fail("Сборка остановлена:\n" + prep.Error);
        var outDir = Path.Combine(folder, "Build", "web");
        try
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(outDir);
                foreach (var f in new[] { "index.html", EngineInfo.ProjectFile }) File.Copy(Path.Combine(folder, f), Path.Combine(outDir, f), true);
                foreach (var d in new[] { "Runtime", "Scenes", "Assets", "lib" }) CopyDir(Path.Combine(folder, d), Path.Combine(outDir, d));
            });
            return EngineResult<string>.Success(outDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return EngineResult<string>.Fail($"Не удалось собрать игру: {ex.Message}"); }
    }

    static void CopyDir(string src, string dst)
    {
        if (!Directory.Exists(src)) return;
        foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(dst, Path.GetRelativePath(src, f));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(f, target, true);
        }
    }
}
