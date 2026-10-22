using Moewshare.Launcher.Models;

namespace Moewshare.Launcher.Services;

public interface ITemplateService
{
    IReadOnlyList<TemplateInfo> GetAll();
    TemplateInfo? Find(string id);
}

public sealed class TemplateService : ITemplateService
{
    const string Ready = "Играбельный (0.1)";
    static readonly TemplateInfo[] Items =
    [
        new("platformer2d", "2D-платформер", "Игрок, гравитация, прыжки, платформы и монеты.", ProjectKind.TwoD, Ready),
        new("adventure3d", "3D-приключение", "3D-мир, игрок-сфера и препятствия.", ProjectKind.ThreeD, Ready),
        new("topdown", "Top-down", "Вид сверху: игрок, стены и ящики.", ProjectKind.TwoD, Ready),
        new("empty", "Пустой 2D-проект", "Стартовая сцена с одним объектом.", ProjectKind.TwoD, "Базовый"),
        new("empty3d", "Пустой 3D-проект", "Стартовая 3D-сцена с одним объектом.", ProjectKind.ThreeD, "Базовый"),
    ];
    public IReadOnlyList<TemplateInfo> GetAll() => Items;
    public TemplateInfo? Find(string id) => Items.FirstOrDefault(t => t.Id == id);
}
