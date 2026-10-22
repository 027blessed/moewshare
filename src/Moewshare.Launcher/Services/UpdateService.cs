namespace Moewshare.Launcher.Services;

public sealed record UpdateCheckResult(bool Available, string? Version, string Message);

/// <summary>
/// Границы будущей системы обновлений. Реализация должна: получить манифест, проверить его подпись,
/// сверить SHA-256 пакета, поддерживать прогресс/отмену и откат прерванной установки.
/// </summary>
public interface IUpdateService
{
    Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default);
}

/// <summary>Сервера обновлений ещё нет: честно сообщаем об этом и ничего не скачиваем.</summary>
public sealed class NoServerUpdateService : IUpdateService
{
    public Task<UpdateCheckResult> CheckAsync(CancellationToken ct = default) =>
        Task.FromResult(new UpdateCheckResult(false, null, "Сервер обновлений ещё не настроен."));
}
