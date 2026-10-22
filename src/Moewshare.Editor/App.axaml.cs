using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Moewshare.Editor.ViewModels;
using Moewshare.Editor.Views;

namespace Moewshare.Editor;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
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
