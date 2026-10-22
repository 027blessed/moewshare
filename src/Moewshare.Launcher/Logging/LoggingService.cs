using Moewshare.Launcher.Configuration;

namespace Moewshare.Launcher.Logging;

public interface ILoggingService
{
    void Info(string message);
    void Error(string message, Exception? ex = null);
}

public sealed class NullLoggingService : ILoggingService
{
    public void Info(string message) { }
    public void Error(string message, Exception? ex = null) { }
}

public sealed class FileLoggingService : ILoggingService
{
    readonly object _lock = new();
    public void Info(string message) => Write("INFO", message);
    public void Error(string message, Exception? ex = null) => Write("ERROR", ex is null ? message : $"{message}\n{ex}");

    void Write(string level, string text)
    {
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(AppPaths.Logs);
                File.AppendAllText(Path.Combine(AppPaths.Logs, $"launcher-{DateTime.Now:yyyyMMdd}.log"),
                    $"{DateTime.Now:HH:mm:ss.fff} [{level}] {text}{Environment.NewLine}");
            }
        }
        catch { /* логирование не должно ронять приложение */ }
    }
}
