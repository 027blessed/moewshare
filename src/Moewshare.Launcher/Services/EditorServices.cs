using System.ComponentModel;
using System.Diagnostics;
using Moewshare.Launcher.Logging;
using Moewshare.Launcher.Models;

namespace Moewshare.Launcher.Services;

public interface IEditorInstallationService
{
    Task<IReadOnlyList<EditorInstall>> ScanAsync();
}

/// <summary>Ищет редакторы в папке установок: &lt;папка&gt;/&lt;версия&gt;/Moewshare.Editor[.exe|.app].</summary>
public sealed class EditorInstallationService(ISettingsService settings) : IEditorInstallationService
{
    public static string ExecutableName =>
        OperatingSystem.IsWindows() ? "Moewshare.Editor.exe"
        : OperatingSystem.IsMacOS() ? "Moewshare.Editor.app" : "Moewshare.Editor";

    public async Task<IReadOnlyList<EditorInstall>> ScanAsync()
    {
        await settings.LoadAsync();
        var roots = new[] { settings.Current.EnginesFolder, Path.Combine(AppContext.BaseDirectory, "Editors") };
        return await Task.Run(() =>
        {
            var list = new List<EditorInstall>();
            foreach (var root in roots.Distinct())
            {
                if (!Directory.Exists(root)) continue;
                foreach (var dir in Directory.EnumerateDirectories(root))
                {
                    var exe = Path.Combine(dir, ExecutableName);
                    var version = Path.GetFileName(dir);
                    if ((File.Exists(exe) || (OperatingSystem.IsMacOS() && Directory.Exists(exe))) && !list.Any(i => i.Version == version))
                        list.Add(new EditorInstall(version, exe));
                }
            }
            return (IReadOnlyList<EditorInstall>)list
                .OrderByDescending(i => Version.TryParse(i.Version, out var v) ? v : new Version(0, 0))
                .ThenByDescending(i => i.Version, StringComparer.OrdinalIgnoreCase).ToList();
        });
    }
}

public interface IEditorLauncherService
{
    Task<OpResult> OpenProjectAsync(ProjectEntry project);
    Task<OpResult> OpenEditorAsync();
}

/// <summary>Контракт запуска: Moewshare.Editor --project "&lt;путь к папке проекта&gt;".</summary>
public sealed class EditorLauncherService(IEditorInstallationService installs, ILoggingService log) : IEditorLauncherService
{
    const string NotInstalled = "Редактор Moewshare не установлен. Укажите папку установок в настройках.";

    public async Task<OpResult> OpenProjectAsync(ProjectEntry p)
    {
        if (p.HasProblem) return OpResult.Fail($"Проект недоступен: {p.StatusText}.");
        var list = await installs.ScanAsync();
        if (list.Count == 0) return OpResult.Fail(NotInstalled);
        var editor = list.FirstOrDefault(i => i.Version == p.EngineVersion) ?? list[0];
        return await LaunchAsync(editor, p.Folder);
    }

    public async Task<OpResult> OpenEditorAsync()
    {
        var list = await installs.ScanAsync();
        return list.Count == 0 ? OpResult.Fail(NotInstalled) : await LaunchAsync(list[0], null);
    }

    Task<OpResult> LaunchAsync(EditorInstall editor, string? projectFolder) => Task.Run(() =>
    {
        var exe = editor.ExecutablePath;
        if (!File.Exists(exe) && !Directory.Exists(exe))
            return OpResult.Fail($"Файл редактора не найден: {exe}");
        if (projectFolder is not null && !Directory.Exists(projectFolder))
            return OpResult.Fail("Папка проекта не найдена.");

        var psi = new ProcessStartInfo { UseShellExecute = false };
        if (OperatingSystem.IsMacOS() && Directory.Exists(exe))
        {
            psi.FileName = "open";
            foreach (var a in new[] { "-n", "-a", exe }) psi.ArgumentList.Add(a);
            if (projectFolder is not null) { psi.ArgumentList.Add("--args"); psi.ArgumentList.Add("--project"); psi.ArgumentList.Add(projectFolder); }
        }
        else
        {
            psi.FileName = exe;
            psi.WorkingDirectory = Path.GetDirectoryName(exe)!;
            if (projectFolder is not null) { psi.ArgumentList.Add("--project"); psi.ArgumentList.Add(projectFolder); }
        }

        try
        {
            using var _ = Process.Start(psi);
            log.Info($"Запущен редактор {editor.Version}: {projectFolder ?? "(без проекта)"}");
            return OpResult.Success;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            log.Error("Не удалось запустить редактор", ex);
            return OpResult.Fail($"Не удалось запустить редактор: {ex.Message}");
        }
    });
}
