using BaseSiteSolution.Infrastructure.Smidge;
using BaseSiteSolution.Infrastructure.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace BaseSiteSolution.Infrastructure.Notifications;

/// <summary>
/// Notification Handler для отслеживания публикации страницы themeEditor
/// При публикации генерирует CSS файл с переменными стилей, обновляет версию кеш-бастера и очищает кеш Smidge
/// </summary>
public class ThemeEditorPublishedNotificationHandler : INotificationHandler<ContentPublishedNotification>
{
    private readonly DynamicCacheBuster _cacheBuster;
    private readonly SmidgeCacheService _cacheService;
    private readonly ThemeCssGeneratorService _cssGeneratorService;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<ThemeEditorPublishedNotificationHandler> _logger;
    private readonly IAppCache? _appCache;

    public ThemeEditorPublishedNotificationHandler(
        DynamicCacheBuster cacheBuster,
        SmidgeCacheService cacheService,
        ThemeCssGeneratorService cssGeneratorService,
        IWebHostEnvironment environment,
        ILogger<ThemeEditorPublishedNotificationHandler> logger,
        IAppCache? appCache = null)
    {
        _cacheBuster = cacheBuster;
        _cacheService = cacheService;
        _cssGeneratorService = cssGeneratorService;
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

        // В Development режиме обновляем только CSS, но не кеш
        var isDevelopment = _environment.IsDevelopment();

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

                    // Генерируем CSS файл с переменными стилей и шрифтами
                    try
                    {
                        _cssGeneratorService.GenerateCssFile(publishedEntity.Id, "css/vars.css");
                        _logger.LogInformation("CSS файл с переменными стилей успешно сгенерирован для контента {ContentId}", publishedEntity.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Ошибка при генерации CSS файла для контента {ContentId}", publishedEntity.Id);
                        // Продолжаем выполнение даже если генерация CSS не удалась
                    }

                    // В Production режиме обновляем кеш-бастер и очищаем кеши
                    if (!isDevelopment)
                    {
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
                            "Development режим: CSS файл сгенерирован, кеш не обновлен (ID: {ContentId})",
                            publishedEntity.Id);
                    }
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
