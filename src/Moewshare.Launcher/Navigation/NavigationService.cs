using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace Moewshare.Launcher.Navigation;

public interface IPage { Task InitializeAsync(); }

public interface INavigationService
{
    ObservableObject? Current { get; }
    Task NavigateAsync<T>(Action<T>? init = null) where T : ObservableObject;
}

public sealed class NavigationService(IServiceProvider services) : ObservableObject, INavigationService
{
    ObservableObject? _current;
    public ObservableObject? Current { get => _current; private set => SetProperty(ref _current, value); }

    public async Task NavigateAsync<T>(Action<T>? init = null) where T : ObservableObject
    {
        var vm = services.GetRequiredService<T>();   // страницы — transient, данные обновляются при каждом заходе
        init?.Invoke(vm);
        Current = vm;
        if (vm is IPage page) await page.InitializeAsync();
    }
}
