using Avalonia.Media;

namespace Moewshare.Editor.ViewModels;

public enum ConsoleLevel { Info, Warning, Error, Success }

public sealed class ConsoleEntry
{
    public DateTime Time { get; init; } = DateTime.Now;
    public ConsoleLevel Level { get; init; } = ConsoleLevel.Info;
    public string Message { get; init; } = "";

    public string TimeFormatted => Time.ToString("HH:mm:ss");

    public string Icon => Level switch
    {
        ConsoleLevel.Error => "❌",
        ConsoleLevel.Warning => "⚠️",
        ConsoleLevel.Success => "✅",
        _ => "ℹ️"
    };

    public IBrush BadgeBrush => Level switch
    {
        ConsoleLevel.Error => new SolidColorBrush(Color.Parse("#E05263")),
        ConsoleLevel.Warning => new SolidColorBrush(Color.Parse("#F39C12")),
        ConsoleLevel.Success => new SolidColorBrush(Color.Parse("#27AE60")),
        _ => new SolidColorBrush(Color.Parse("#8E44AD"))
    };

    public string DisplayText => $"[{TimeFormatted}] {Icon} {Message}";
}
