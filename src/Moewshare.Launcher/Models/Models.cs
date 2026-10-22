using System.Text.Json.Serialization;
using Moewshare.Launcher.Configuration;

namespace Moewshare.Launcher.Models;

public enum ProjectStatus { Ok, Missing, Corrupt }

public sealed record OpResult(bool Ok, string? Error = null)
{
    public static OpResult Success { get; } = new(true);
    public static OpResult Fail(string error) => new(false, error);
}

public sealed record OpResult<T>(bool Ok, T? Value, string? Error = null)
{
    public static OpResult<T> Success(T value) => new(true, value);
    public static OpResult<T> Fail(string error) => new(false, default, error);
}

public sealed class AppSettings
{
    public string ProjectsFolder { get; set; } = AppPaths.DefaultProjects;
    public string EnginesFolder { get; set; } = AppPaths.DefaultEditors;
    public string Language { get; set; } = "ru";
    public bool AutoCheckUpdates { get; set; } = true;
}

public sealed class ProjectEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Folder { get; set; } = "";
    public ProjectKind Kind { get; set; }
    public string? EngineVersion { get; set; }
    public DateTime LastModified { get; set; } = DateTime.Now;
    public bool IsFavorite { get; set; }

    [JsonIgnore] public ProjectStatus Status { get; set; }
    [JsonIgnore] public string KindLabel => Kind == ProjectKind.TwoD ? "2D" : "3D";
    [JsonIgnore] public string Subtitle => $"{KindLabel} · {EngineVersion ?? "0.1.0"} · {LastModified:dd.MM.yyyy HH:mm}";
    [JsonIgnore] public bool HasProblem => Status != ProjectStatus.Ok;
    [JsonIgnore] public bool IsMissing => Status == ProjectStatus.Missing;
    [JsonIgnore] public string KindIcon => Kind == ProjectKind.TwoD ? "🎮" : "🌐";
    [JsonIgnore] public string DisplayDate => LastModified.ToString("dd.MM.yyyy HH:mm");
    [JsonIgnore] public string ShortFolder => Folder.Length > 36 ? "..." + Folder[^34..] : Folder;
    [JsonIgnore] public string? StatusText => Status switch
    {
        ProjectStatus.Missing => "Папка проекта не найдена на диске",
        ProjectStatus.Corrupt => "Проект повреждён",
        _ => null
    };
}

public sealed record TemplateInfo(string Id, string Name, string Description, ProjectKind Kind, string Status)
{
    public string KindLabel => Kind == ProjectKind.TwoD ? "2D" : "3D";
}

public sealed record EditorInstall(string Version, string ExecutablePath);
