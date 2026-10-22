using Avalonia.Controls;
using Avalonia.Threading;

namespace Moewshare.Editor.Views;

public partial class SplashScreenWindow : Window
{
    public SplashScreenWindow()
    {
        InitializeComponent();
    }

    public void SetProjectInfo(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            ProjectTitleText.Text = "Новый проект";
            ProjectFolderText.Text = "Временная рабочая среда";
        }
        else
        {
            var name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            ProjectTitleText.Text = $"Проект: {name}";
            ProjectFolderText.Text = folder;
        }
    }

    public void SetProgress(string message, double percent)
    {
        Dispatcher.UIThread.Post(() =>
        {
            StatusMessageText.Text = message;
            LoadingProgressBar.Value = percent;
            ProgressPercentText.Text = $"{(int)percent}%";
        });
    }

    public async Task RunLoadingSequenceAsync(string? folder, Func<Task> initializeAction)
    {
        SetProjectInfo(folder);

        SetProgress("Инициализация подсистем движка .NET 10...", 15);
        await Task.Delay(200);

        SetProgress("Загрузка графического пайплайна и Avalonia...", 35);
        await Task.Delay(200);

        SetProgress("Подготовка рантайма Babylon.js и шаблонов сцен...", 55);
        await Task.Delay(220);

        SetProgress("Чтение конфигурации проекта и кэширование текстур...", 75);
        await initializeAction();
        await Task.Delay(200);

        SetProgress("Построение панелей редактора и запуск сцены...", 95);
        await Task.Delay(180);

        SetProgress("Готово!", 100);
        await Task.Delay(120);
    }
}
