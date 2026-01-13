using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Smidge;
using System.IO;

namespace BaseSiteSolution.Infrastructure.Smidge;

/// <summary>
/// Сервис для очистки кеша Smidge
/// </summary>
public class SmidgeCacheService
{
    private readonly ILogger<SmidgeCacheService> _logger;
    private readonly IWebHostEnvironment _environment;
    private readonly IBundleManager? _bundleManager;
    private readonly IMemoryCache? _memoryCache;

    public SmidgeCacheService(
        ILogger<SmidgeCacheService> logger,
        IWebHostEnvironment environment,
        IBundleManager? bundleManager = null,
        IMemoryCache? memoryCache = null)
    {
        _logger = logger;
        _environment = environment;
        _bundleManager = bundleManager;
        _memoryCache = memoryCache;
    }

    /// <summary>
    /// Очищает весь кеш Smidge (синхронная версия)
    /// </summary>
    public void ClearCache()
    {
        try
        {
            _logger.LogInformation("Начинаем очистку кеша Smidge");

            // Очищаем IMemoryCache, если доступен (Smidge может использовать его для кеширования URL)
            if (_memoryCache != null)
            {
                try
                {
                    // Пытаемся найти и удалить ключи кеша, связанные со Smidge
                    // Smidge может использовать ключи вида "smidge:*" или "bundle:*"
                    if (_memoryCache is MemoryCache memoryCache)
                    {
                        // Используем рефлексию для доступа к внутреннему словарю кеша
                        var field = typeof(MemoryCache).GetField("_entries",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (field != null)
                        {
                            var entries = field.GetValue(memoryCache) as System.Collections.IDictionary;
                            if (entries != null)
                            {
                                var keysToRemove = new List<object>();
                                foreach (var key in entries.Keys)
                                {
                                    var keyString = key?.ToString() ?? "";
                                    // Удаляем все ключи, связанные со Smidge
                                    if (keyString.Contains("smidge", StringComparison.OrdinalIgnoreCase) ||
                                        keyString.Contains("bundle", StringComparison.OrdinalIgnoreCase))
                                    {
                                        keysToRemove.Add(key);
                                    }
                                }
                                foreach (var key in keysToRemove)
                                {
                                    _memoryCache.Remove(key);
                                    _logger.LogInformation("Удален ключ из IMemoryCache: {Key}", key);
                                }
                                _logger.LogInformation("Очищено {Count} ключей из IMemoryCache, связанных со Smidge", keysToRemove.Count);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Не удалось очистить IMemoryCache для Smidge: {Error}", ex.Message);
                }
            }

            // Удаляем физическую папку с кешем, если она существует
            // Это основной способ очистки кеша в Smidge 2.0.0
            var cachePath = Path.Combine(_environment.ContentRootPath, "Smidge", "Cache");
            if (Directory.Exists(cachePath))
            {
                try
                {
                    // Удаляем все подпапки в Cache
                    var directories = Directory.GetDirectories(cachePath);
                    foreach (var directory in directories)
                    {
                        Directory.Delete(directory, recursive: true);
                        _logger.LogInformation("Удалена папка кеша: {Directory}", directory);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Не удалось удалить физическую папку кеша: {CachePath}", cachePath);
                }
            }

            _logger.LogInformation("Очистка кеша Smidge завершена успешно");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при очистке кеша Smidge");
            throw;
        }
    }

    /// <summary>
    /// Очищает весь кеш Smidge (асинхронная версия для совместимости)
    /// </summary>
    public Task ClearCacheAsync()
    {
        ClearCache();
        return Task.CompletedTask;
    }
}
