using Microsoft.Extensions.Logging;
using Smidge.Cache;
using System.Security.Cryptography;
using System.Text;

namespace BaseSiteSolution.Infrastructure.Smidge;

/// <summary>
/// Кастомный CacheBuster для Smidge, который позволяет динамически обновлять версию
/// без перезапуска приложения. Версия хранится в памяти и может быть обновлена программно.
/// </summary>
public class DynamicCacheBuster : ICacheBuster
{
    private string _cacheValue;
    private readonly object _lockObject = new object();
    private readonly ILogger<DynamicCacheBuster>? _logger;

    public DynamicCacheBuster(ILogger<DynamicCacheBuster>? logger = null)
    {
        _logger = logger;
        // Генерируем начальную версию при создании
        _cacheValue = GenerateCacheValue();
        _logger?.LogInformation("DynamicCacheBuster инициализирован с версией: {Version}", _cacheValue);
    }

    /// <summary>
    /// Получить текущее значение кеш-бастера
    /// </summary>
    public string GetValue()
    {
        // Используем LogInformation вместо LogDebug, чтобы увидеть вызовы в логах
        _logger?.LogInformation("DynamicCacheBuster.GetValue() вызван, возвращаем: {Version}", _cacheValue);
        return _cacheValue;
    }

    /// <summary>
    /// Обновить версию кеш-бастера (вызывается при публикации страницы themeEditor)
    /// </summary>
    public void UpdateVersion()
    {
        lock (_lockObject)
        {
            var oldValue = _cacheValue;
            _cacheValue = GenerateCacheValue();
            _logger?.LogInformation(
                "DynamicCacheBuster.UpdateVersion() вызван: {OldVersion} -> {NewVersion}",
                oldValue,
                _cacheValue);
        }
    }

    /// <summary>
    /// Генерирует случайный хеш для версии кеш-бастера
    /// Использует SHA256 для генерации короткого хеша (8 символов)
    /// </summary>
    private string GenerateCacheValue()
    {
        // Генерируем случайный хеш на основе текущего времени и случайных данных
        var data = $"{DateTime.UtcNow.Ticks}{Guid.NewGuid()}";
        var bytes = Encoding.UTF8.GetBytes(data);

        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(bytes);

        // Берем первые 8 байт и конвертируем в hex строку (16 символов)
        // Затем берем первые 8 символов для короткого хеша
        var hexString = Convert.ToHexString(hash).ToLowerInvariant();
        return hexString.Substring(0, 8);
    }
}
