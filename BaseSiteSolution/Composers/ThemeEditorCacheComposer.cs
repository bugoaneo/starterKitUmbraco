using BaseSiteSolution.Infrastructure.Notifications;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;

namespace BaseSiteSolution.Composers;

/// <summary>
/// Composer для регистрации Notification Handler для страницы themeEditor
/// </summary>
public class ThemeEditorCacheComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        // Регистрируем Notification Handler для отслеживания публикации страницы themeEditor
        builder.AddNotificationHandler<ContentPublishedNotification, ThemeEditorPublishedNotificationHandler>();
    }
}
