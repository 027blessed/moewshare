using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Moewshare.Launcher.Configuration;
using Moewshare.Launcher.Logging;
using Moewshare.Launcher.Navigation;
using Moewshare.Launcher.Repositories;
using Moewshare.Launcher.Services;
using Moewshare.Launcher.ViewModels;
using Moewshare.Launcher.Views;

namespace Moewshare.Launcher;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var sc = new ServiceCollection();
        sc.AddSingleton<ILoggingService, FileLoggingService>();
        sc.AddSingleton<ISettingsService>(_ => new SettingsService(AppPaths.SettingsFile));
        sc.AddSingleton<IProjectRegistryService>(_ => new ProjectRegistryService(AppPaths.RegistryFile));
        sc.AddSingleton<IProjectService, ProjectService>();
        sc.AddSingleton<ITemplateService, TemplateService>();
        sc.AddSingleton<IEditorInstallationService, EditorInstallationService>();
        sc.AddSingleton<IEditorLauncherService, EditorLauncherService>();
        sc.AddSingleton<IUpdateService, NoServerUpdateService>();
        sc.AddSingleton<IDialogService, DialogService>();
        sc.AddSingleton<INavigationService, NavigationService>();
        sc.AddSingleton<MainViewModel>();
        sc.AddTransient<HomeViewModel>();
        sc.AddTransient<ProjectsViewModel>();
        sc.AddTransient<NewProjectViewModel>();
        sc.AddTransient<LibraryViewModel>();
        sc.AddTransient<SettingsViewModel>();
        var services = sc.BuildServiceProvider();

        var log = services.GetRequiredService<ILoggingService>();
        AppDomain.CurrentDomain.UnhandledException += (_, e) => log.Error("Необработанное исключение", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => log.Error("Необработанное исключение в задаче", e.Exception);

        ThemeService.LoadAndApply();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = services.GetRequiredService<MainViewModel>();
            desktop.MainWindow = new MainWindow { DataContext = vm };
            desktop.Exit += (_, _) => services.Dispose();
            _ = vm.InitializeAsync();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
