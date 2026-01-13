using BaseSiteSolution.Infrastructure.Smidge;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace BaseSiteSolution.Infrastructure.Notifications;

/// <summary>
/// Notification Handler для отслеживания публикации страницы themeEditor
/// При публикации обновляет версию кеш-бастера и очищает кеш Smidge
/// </summary>
public class ThemeEditorPublishedNotificationHandler : INotificationHandler<ContentPublishedNotification>
{
    private readonly DynamicCacheBuster _cacheBuster;
    private readonly SmidgeCacheService _cacheService;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<ThemeEditorPublishedNotificationHandler> _logger;
    private readonly IAppCache? _appCache;

    public ThemeEditorPublishedNotificationHandler(
        DynamicCacheBuster cacheBuster,
        SmidgeCacheService cacheService,
        IWebHostEnvironment environment,
        ILogger<ThemeEditorPublishedNotificationHandler> logger,
        IAppCache? appCache = null)
    {
        _cacheBuster = cacheBuster;
        _cacheService = cacheService;
        _environment = environment;
        _logger = logger;
        _appCache = appCache;
    }

    public void Handle(ContentPublishedNotification notification)
    {
        _logger.LogInformation(
            "ThemeEditorPublishedNotificationHandler: получено уведомление о публикации. Количество сущностей: {Count}, Environment: {Environment}",
            notification.PublishedEntities.Count(),
            _environment.EnvironmentName);

        // Работаем только в Production режиме
        if (_environment.IsDevelopment())
        {
            _logger.LogInformation("ThemeEditorPublishedNotificationHandler: пропущено (Development режим)");
            return;
        }

        foreach (var publishedEntity in notification.PublishedEntities)
        {
            try
            {
                _logger.LogInformation(
                    "ThemeEditorPublishedNotificationHandler: проверка сущности ID: {ContentId}, Alias: {Alias}",
                    publishedEntity.Id,
                    publishedEntity.ContentType.Alias);

                // Проверяем alias ContentType напрямую из publishedEntity
                if (publishedEntity.ContentType.Alias == "themeEditor")
                {
                    _logger.LogInformation(
                        "Обнаружена публикация страницы themeEditor (ID: {ContentId}, Key: {ContentKey})",
                        publishedEntity.Id,
                        publishedEntity.Key);

                    // Получаем старую версию для логирования
                    var oldVersion = _cacheBuster.GetValue();

                    // Обновляем версию кеш-бастера
                    _cacheBuster.UpdateVersion();
                    var newVersion = _cacheBuster.GetValue();

                    _logger.LogInformation(
                        "Версия кеш-бастера обновлена: {OldVersion} -> {NewVersion}",
                        oldVersion,
                        newVersion);

                    // Очищаем кеш Smidge (синхронно, так как Handle не async)
                    _cacheService.ClearCache();

                    // Очищаем кеш Umbraco (output cache и content cache)
                    if (_appCache != null)
                    {
                        try
                        {
                            _appCache.Clear();
                            _logger.LogInformation("Кеш Umbraco (IAppCache) очищен");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Не удалось очистить кеш Umbraco");
                        }
                    }

                    _logger.LogInformation(
                        "Кеш Smidge очищен после публикации страницы themeEditor (ID: {ContentId})",
                        publishedEntity.Id);
                }
                else
                {
                    _logger.LogInformation(
                        "ThemeEditorPublishedNotificationHandler: сущность ID {ContentId} не является themeEditor (Alias: {Alias})",
                        publishedEntity.Id,
                        publishedEntity.ContentType.Alias);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Ошибка при обработке публикации контента (ID: {ContentId})",
                    publishedEntity.Id);
            }
        }
    }
}
