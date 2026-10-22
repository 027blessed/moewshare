using CommunityToolkit.Mvvm.ComponentModel;
using Moewshare.Launcher.Models;
using Moewshare.Launcher.Navigation;
using Moewshare.Launcher.Services;

namespace Moewshare.Launcher.ViewModels;

public sealed partial class LibraryViewModel(ITemplateService templates) : ObservableObject, IPage
{
    public IReadOnlyList<string> Categories { get; } =
        ["Шаблоны игр", "Модели и текстуры", "Материалы", "Звуки", "Компоненты", "Плагины редактора", "SDK платформ"];
    public IReadOnlyList<TemplateInfo> LocalTemplates { get; } = templates.GetAll();
    public string OnlineState => "Онлайн-каталог пока недоступен: сервер каталога ещё не запущен. Ниже — локальные ресурсы лаунчера.";
    public Task InitializeAsync() => Task.CompletedTask;
}
