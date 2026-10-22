namespace Moewshare.Launcher.Services;

public static class PathValidator
{
    static readonly char[] Invalid = [.. Path.GetInvalidFileNameChars(), '\\', '/', ':', '*', '?', '"', '<', '>', '|'];
    static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON","PRN","AUX","NUL","COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9",
        "LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"
    };

    public static string? ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Введите название проекта.";
        if (name.Length > 64) return "Название слишком длинное (максимум 64 символа).";
        if (name.IndexOfAny(Invalid) >= 0) return "Название содержит недопустимые символы: \\ / : * ? \" < > |";
        if (name != name.Trim() || name.EndsWith('.')) return "Название не должно начинаться или заканчиваться пробелом или точкой.";
        if (Reserved.Contains(name)) return "Это имя зарезервировано системой.";
        return null;
    }

    public static string? ValidateFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return "Выберите папку сохранения.";
        try
        {
            if (!Path.IsPathRooted(folder)) return "Укажите полный путь к папке.";
            Path.GetFullPath(folder);
        }
        catch (Exception) { return "Путь к папке некорректен."; }
        return null;
    }
}
