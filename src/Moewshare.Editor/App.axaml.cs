using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Moewshare.Editor.Services;
using Moewshare.Editor.ViewModels;
using Moewshare.Editor.Views;

namespace Moewshare.Editor;

public partial class App : Application
{
    static EditorViewModel? desktopVm;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // Тема (светлая / тёмная / как в системе) — общая с лаунчером
        ThemeManager.LoadAndApply();
        try { Console.Error.WriteLine($"[theme] file={Moewshare.Engine.UserPreferences.ThemeFile} mode={ThemeManager.Mode} actual={ActualThemeVariant}"); } catch { }

        // Ошибка в обработчике интерфейса не должна закрывать редактор и терять несохранённую сцену:
        // пишем её в журнал и в консоль, приложение продолжает работать.
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            CrashLog.Write(e.Exception);
            e.Handled = true;
            if (desktopVm is not null) desktopVm.Say("Внутренняя ошибка: " + e.Exception.Message, ConsoleLevel.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashLog.Write(e.ExceptionObject as Exception);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = desktop.Args ?? [];
            var i = Array.IndexOf(args, "--project");
            var folder = i >= 0 && i + 1 < args.Length ? args[i + 1] : null;

            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var splash = new SplashScreenWindow();
            splash.Show();

            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    var window = new MainWindow();
                    var vm = new EditorViewModel(new WindowUi(window));
                    desktopVm = vm;
                    window.DataContext = vm;
                    window.Attach(vm);

                    await splash.RunLoadingSequenceAsync(folder, async () =>
                    {
                        await vm.StartAsync(folder);
                    });

                    window.Show();
                    desktop.MainWindow = window;
                    desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
                    splash.Close();
                }
                catch (Exception)
                {
                    var window = new MainWindow();
                    var vm = new EditorViewModel(new WindowUi(window));
                    window.DataContext = vm;
                    window.Attach(vm);
                    window.Show();
                    desktop.MainWindow = window;
                    desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
                    splash.Close();
                    _ = vm.StartAsync(folder);
                }
            });
        }
        base.OnFrameworkInitializationCompleted();
    }
}

/// <summary>Журнал необработанных ошибок: &lt;данные Moewshare&gt;/Logs/editor-errors.log и stderr.</summary>
static class CrashLog
{
    public static void Write(Exception? ex)
    {
        if (ex is null) return;
        var text = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n";
        try { Console.Error.WriteLine(text); } catch { }
        try
        {
            var dir = Path.Combine(Moewshare.Engine.UserPreferences.DataRoot, "Logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "editor-errors.log"), text);
        }
        catch { }
    }
}
