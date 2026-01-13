using BaseSiteSolution.Infrastructure.Smidge;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Smidge.Cache;

namespace BaseSiteSolution.Infrastructure.Smidge;

/// <summary>
/// Tag Helper для добавления версии кеш-бастера к URL бандлов Smidge
/// Используется как обходной путь, когда Smidge кеширует URL и не вызывает GetValue() при каждом запросе
/// </summary>
[HtmlTargetElement("link", Attributes = "smidge-version")]
[HtmlTargetElement("script", Attributes = "smidge-version")]
public class SmidgeVersionTagHelper : TagHelper
{
    private readonly DynamicCacheBuster _cacheBuster;

    public SmidgeVersionTagHelper(DynamicCacheBuster cacheBuster)
    {
        _cacheBuster = cacheBuster;
    }

    /// <summary>
    /// Атрибут для включения добавления версии к URL
    /// </summary>
    [HtmlAttributeName("smidge-version")]
    public bool AddVersion { get; set; }

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (!AddVersion)
        {
            return;
        }

        // Получаем текущий href или src
        var urlAttribute = output.TagName == "link" ? "href" : "src";
        if (output.Attributes.TryGetAttribute(urlAttribute, out var attribute))
        {
            var url = attribute.Value?.ToString() ?? "";
            if (!string.IsNullOrEmpty(url))
            {
                // Добавляем версию кеш-бастера к URL
                var version = _cacheBuster.GetValue();
                var separator = url.Contains("?") ? "&" : "?";
                var newUrl = $"{url}{separator}v={version}";

                // Обновляем атрибут
                output.Attributes.SetAttribute(urlAttribute, newUrl);
            }
        }
    }
}
