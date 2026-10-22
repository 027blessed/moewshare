using System.Text.Json;
using System.Text.Json.Serialization;

namespace Moewshare.Launcher.Repositories;

/// <summary>JSON-файл с атомарным сохранением (tmp → replace) и восстановлением из .bak.</summary>
public sealed class JsonFileStore<T>(string path) where T : class, new()
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<T?> TryReadAsync(string file)
    {
        try
        {
            if (!File.Exists(file)) return null;
            await using var s = File.OpenRead(file);
            return await JsonSerializer.DeserializeAsync<T>(s, Options);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public async Task<T> LoadAsync()
    {
        if (!File.Exists(path))
            return await TryReadAsync(path + ".bak") ?? new T();

        var data = await TryReadAsync(path);
        if (data is not null) return data;

        // Файл повреждён: сохраняем его для разбора и пробуем резервную копию.
        try { File.Move(path, $"{path}.corrupt-{DateTime.Now:yyyyMMddHHmmss}"); } catch { }
        return await TryReadAsync(path + ".bak") ?? new T();
    }

    public async Task SaveAsync(T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        await using (var s = File.Create(tmp))
            await JsonSerializer.SerializeAsync(s, value, Options);
        if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
        else File.Move(tmp, path);
    }
}
