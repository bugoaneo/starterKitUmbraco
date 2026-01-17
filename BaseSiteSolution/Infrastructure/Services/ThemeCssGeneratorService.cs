using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core;

namespace BaseSiteSolution.Infrastructure.Services;

/// <summary>
/// Сервис для генерации CSS файла с переменными стилей из страницы themeEditor
/// Обрабатывает CSS переменные, размеры шрифтов и кастомные шрифты из ZIP-архивов
/// </summary>
public class ThemeCssGeneratorService
{
    private readonly IWebHostEnvironment _environment;
    private readonly IMediaService _mediaService;
    private readonly IContentService _contentService;
    private readonly ILogger<ThemeCssGeneratorService> _logger;

    // Расширения файлов шрифтов
    private static readonly HashSet<string> FontExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".woff", ".woff2", ".ttf", ".otf", ".eot", ".svg"
    };

    public ThemeCssGeneratorService(
        IWebHostEnvironment environment,
        IMediaService mediaService,
        IContentService contentService,
        ILogger<ThemeCssGeneratorService> logger)
    {
        _environment = environment;
        _mediaService = mediaService;
        _contentService = contentService;
        _logger = logger;
    }

    /// <summary>
    /// Генерирует CSS файл на основе свойств страницы themeEditor
    /// </summary>
    /// <param name="contentId">ID страницы themeEditor</param>
    /// <param name="outputPath">Путь для сохранения CSS файла (относительно wwwroot)</param>
    public void GenerateCssFile(int contentId, string outputPath = "css/vars.css")
    {
        try
        {
            var content = _contentService.GetById(contentId);
            if (content == null || content.ContentType.Alias != "themeEditor")
            {
                _logger.LogWarning("Контент с ID {ContentId} не найден или не является themeEditor", contentId);
                return;
            }

            // Обрабатываем шрифты из ZIP-архивов (извлекаем в папку fonts)
            ProcessFonts(content);

            var cssBuilder = new StringBuilder();
            cssBuilder.AppendLine("/* Автоматически сгенерированный файл стилей */");
            cssBuilder.AppendLine("/* Не редактируйте вручную! */");
            cssBuilder.AppendLine();

            // Генерируем CSS переменные
            cssBuilder.AppendLine(":root {");
            GenerateCssVariables(content, cssBuilder);
            cssBuilder.AppendLine("}");

            // Сохраняем файл
            var fullPath = Path.Combine(_environment.WebRootPath, outputPath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            System.IO.File.WriteAllText(fullPath, cssBuilder.ToString(), Encoding.UTF8);
            _logger.LogInformation("CSS файл успешно сгенерирован: {Path}", fullPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при генерации CSS файла для контента {ContentId}", contentId);
            throw;
        }
    }

    /// <summary>
    /// Обрабатывает ZIP-архивы со шрифтами и извлекает их в папку fonts
    /// </summary>
    private void ProcessFonts(IContent content)
    {
        var downloadFontsProperty = content.Properties.FirstOrDefault(p => p.Alias == "downLoadFonts");

        if (downloadFontsProperty == null || downloadFontsProperty.GetValue() == null)
        {
            return;
        }

        try
        {
            // MediaPicker3 возвращает JSON строку с массивом UDI
            var fontValue = downloadFontsProperty.GetValue()?.ToString();
            _logger.LogInformation("ProcessFonts: значение свойства downLoadFonts = {FontValue}", fontValue);

            if (string.IsNullOrEmpty(fontValue))
            {
                _logger.LogWarning("ProcessFonts: значение свойства downLoadFonts пусто");
                return;
            }

            // Парсим JSON для получения UDI медиафайлов
            var mediaUdis = ParseMediaPickerValue(fontValue);
            if (mediaUdis == null || !mediaUdis.Any())
            {
                _logger.LogWarning("ProcessFonts: не удалось распарсить UDI из значения: {FontValue}", fontValue);
                return;
            }

            _logger.LogInformation("ProcessFonts: найдено {Count} UDI медиафайлов", mediaUdis.Count);

            var fontsDirectory = Path.Combine(_environment.WebRootPath, "fonts");
            if (!Directory.Exists(fontsDirectory))
            {
                Directory.CreateDirectory(fontsDirectory);
            }

            foreach (var mediaUdi in mediaUdis)
            {
                try
                {
                    // Проверяем, что UDI является GuidUdi для получения Guid
                    if (mediaUdi is not GuidUdi guidUdi)
                    {
                        _logger.LogWarning("Медиафайл UDI не является GuidUdi: {MediaUdi}", mediaUdi);
                        continue;
                    }

                    var media = _mediaService.GetById(guidUdi.Guid);
                    if (media == null)
                    {
                        _logger.LogWarning("Медиафайл с ID {MediaId} не найден", guidUdi.Guid);
                        continue;
                    }

                    _logger.LogInformation("ProcessFonts: найден медиафайл ID={MediaId}, Name={MediaName}", guidUdi.Guid, media.Name);

                    // Получаем путь к файлу из свойства umbracoFile
                    // umbracoFile может быть строкой или JSON объектом с полями src и т.д.
                    var umbracoFileValue = media.GetValue("umbracoFile");
                    _logger.LogInformation("ProcessFonts: umbracoFile значение (тип={Type}): {Value}",
                        umbracoFileValue?.GetType().Name ?? "null", umbracoFileValue);

                    if (umbracoFileValue == null)
                    {
                        _logger.LogWarning("ProcessFonts: свойство umbracoFile пусто для медиафайла {MediaId}", guidUdi.Guid);
                        continue;
                    }

                    string mediaPath;
                    if (umbracoFileValue is string strValue)
                    {
                        mediaPath = strValue;
                    }
                    else if (umbracoFileValue is System.Text.Json.JsonElement jsonElement)
                    {
                        // Если это JSON, пытаемся получить src
                        if (jsonElement.TryGetProperty("src", out var srcProp))
                        {
                            mediaPath = srcProp.GetString() ?? string.Empty;
                        }
                        else
                        {
                            mediaPath = jsonElement.GetString() ?? string.Empty;
                        }
                    }
                    else
                    {
                        mediaPath = umbracoFileValue.ToString() ?? string.Empty;
                    }

                    if (string.IsNullOrEmpty(mediaPath))
                    {
                        _logger.LogWarning("ProcessFonts: mediaPath пуст после парсинга umbracoFile");
                        continue;
                    }

                    _logger.LogInformation("ProcessFonts: mediaPath = {MediaPath}", mediaPath);

                    // Получаем физический путь к файлу
                    var physicalPath = GetMediaPhysicalPath(mediaPath);
                    _logger.LogInformation("ProcessFonts: physicalPath = {PhysicalPath}", physicalPath);

                    if (string.IsNullOrEmpty(physicalPath))
                    {
                        _logger.LogWarning("ProcessFonts: physicalPath пуст");
                        continue;
                    }

                    if (!System.IO.File.Exists(physicalPath))
                    {
                        _logger.LogWarning("ProcessFonts: файл медиа не найден по пути: {Path}", physicalPath);
                        continue;
                    }

                    _logger.LogInformation("ProcessFonts: файл найден, размер = {Size} байт", new System.IO.FileInfo(physicalPath).Length);

                    // Проверяем, что это ZIP-архив
                    if (!Path.GetExtension(physicalPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning("Файл {FileName} не является ZIP-архивом", media.Name);
                        continue;
                    }

                    // Извлекаем шрифты из ZIP в папку fonts
                    _logger.LogInformation("ProcessFonts: начинаем извлечение шрифтов из {ZipPath} в {FontsDirectory}", physicalPath, fontsDirectory);
                    ExtractFontsFromZip(physicalPath, fontsDirectory);
                    _logger.LogInformation("ProcessFonts: извлечение шрифтов завершено");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при обработке медиафайла {MediaUdi}", mediaUdi);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при обработке шрифтов из свойства downLoadFonts: {Message}", ex.Message);
            _logger.LogError(ex, "StackTrace: {StackTrace}", ex.StackTrace);
        }
    }

    /// <summary>
    /// Парсит значение MediaPicker3 и извлекает UDI медиафайлов
    /// </summary>
    private List<Udi>? ParseMediaPickerValue(string value)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var result = new List<Udi>();

            // MediaPicker3 может возвращать:
            // 1. JSON массив объектов с полями key, mediaKey, mediaTypeAlias
            // 2. JSON массив строк UDI (устаревший формат)
            // 3. Одна строка UDI

            if (value.TrimStart().StartsWith("["))
            {
                // Пытаемся распарсить как массив объектов MediaPicker3
                try
                {
                    var mediaItems = System.Text.Json.JsonSerializer.Deserialize<List<MediaPicker3Item>>(value);
                    if (mediaItems != null)
                    {
                        foreach (var item in mediaItems)
                        {
                            // Используем mediaKey для получения UDI медиафайла
                            if (!string.IsNullOrEmpty(item.MediaKey))
                            {
                                // Формируем UDI из mediaKey (GUID)
                                if (Guid.TryParse(item.MediaKey, out var mediaGuid))
                                {
                                    var udi = new GuidUdi("media", mediaGuid);
                                    result.Add(udi);
                                    _logger.LogInformation("ParseMediaPickerValue: найден UDI из mediaKey: {MediaKey} -> {Udi}", item.MediaKey, udi);
                                }
                            }
                        }
                    }
                }
                catch (System.Text.Json.JsonException)
                {
                    // Если не получилось как объекты, пытаемся как массив строк
                    try
                    {
                        var mediaItems = System.Text.Json.JsonSerializer.Deserialize<string[]>(value);
                        if (mediaItems != null)
                        {
                            foreach (var item in mediaItems)
                            {
                                if (UdiParser.TryParse(item, out var udi))
                                {
                                    result.Add(udi);
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Игнорируем ошибку
                    }
                }
            }
            else
            {
                // Одна строка UDI
                if (UdiParser.TryParse(value, out var udi))
                {
                    result.Add(udi);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при парсинге значения MediaPicker3: {Value}", value);
            return null;
        }
    }

    /// <summary>
    /// Класс для десериализации объекта MediaPicker3
    /// </summary>
    private class MediaPicker3Item
    {
        public string? Key { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("mediaKey")]
        public string? MediaKey { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("mediaTypeAlias")]
        public string? MediaTypeAlias { get; set; }

        public object[]? Crops { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("focalPoint")]
        public object? FocalPoint { get; set; }
    }

    /// <summary>
    /// Получает физический путь к медиафайлу
    /// </summary>
    private string GetMediaPhysicalPath(string mediaPath)
    {
        // Убираем начальные слеши
        var cleanPath = mediaPath.TrimStart('/', '\\');

        // Если путь начинается с "media/", используем WebRootPath
        // Если нет, считаем что это уже относительный путь от wwwroot
        if (cleanPath.StartsWith("media/", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(_environment.WebRootPath, cleanPath);
        }

        // Если путь абсолютный, возвращаем как есть (но обычно не должно быть)
        if (Path.IsPathRooted(cleanPath))
        {
            return cleanPath;
        }

        // Иначе относительный путь от wwwroot
        return Path.Combine(_environment.WebRootPath, cleanPath);
    }

    /// <summary>
    /// Извлекает шрифты из ZIP-архива в папку fonts (без генерации CSS)
    /// </summary>
    private void ExtractFontsFromZip(string zipPath, string outputDirectory)
    {
        try
        {
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in archive.Entries)
                {
                    // Пропускаем директории (если FullName заканчивается на / и нет точки в имени)
                    if (entry.FullName.EndsWith('/') || (entry.FullName.Contains('/') && !entry.Name.Contains('.')))
                    {
                        continue;
                    }

                    var extension = Path.GetExtension(entry.Name);
                    if (!FontExtensions.Contains(extension))
                    {
                        continue;
                    }

                    // Очищаем имя файла от недопустимых символов
                    var outputFileName = SanitizeFileName(entry.Name);
                    var outputPath = Path.Combine(outputDirectory, outputFileName);

                    try
                    {
                        // Извлекаем файл
                        entry.ExtractToFile(outputPath, overwrite: true);
                        _logger.LogInformation("Шрифт извлечен: {FileName} -> {OutputPath}", entry.Name, outputPath);
                    }
                    catch (DirectoryNotFoundException)
                    {
                        // Если директории нет, создаем её
                        Directory.CreateDirectory(outputDirectory);
                        entry.ExtractToFile(outputPath, overwrite: true);
                        _logger.LogInformation("Шрифт извлечен: {FileName} -> {OutputPath}", entry.Name, outputPath);
                    }
                }
            }

            _logger.LogInformation("Шрифты успешно извлечены из ZIP: {ZipPath} в папку {OutputDirectory}", zipPath, outputDirectory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при извлечении шрифтов из ZIP: {ZipPath}", zipPath);
            throw;
        }
    }

    /// <summary>
    /// Извлекает имя шрифта из имени файла
    /// </summary>
    private string ExtractFontName(string fileName)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);

        // Убираем суффиксы веса и стиля
        nameWithoutExt = Regex.Replace(nameWithoutExt, @"-(bold|regular|light|medium|semibold|thin|extralight|black|heavy|normal|italic|oblique)$", "", RegexOptions.IgnoreCase);
        nameWithoutExt = Regex.Replace(nameWithoutExt, @"-\d+$", ""); // Убираем числа в конце

        return nameWithoutExt.Trim().Replace("-", " ");
    }

    /// <summary>
    /// Извлекает вес шрифта из имени файла
    /// </summary>
    private string ExtractFontWeight(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();

        if (name.Contains("thin") || name.Contains("100"))
            return "100";
        if (name.Contains("extralight") || name.Contains("200"))
            return "200";
        if (name.Contains("light") || name.Contains("300"))
            return "300";
        if (name.Contains("regular") || name.Contains("400") || name.Contains("normal"))
            return "400";
        if (name.Contains("medium") || name.Contains("500"))
            return "500";
        if (name.Contains("semibold") || name.Contains("600"))
            return "600";
        if (name.Contains("bold") || name.Contains("700"))
            return "700";
        if (name.Contains("extrabold") || name.Contains("800"))
            return "800";
        if (name.Contains("black") || name.Contains("heavy") || name.Contains("900"))
            return "900";

        return "400"; // По умолчанию
    }

    /// <summary>
    /// Извлекает стиль шрифта из имени файла
    /// </summary>
    private string ExtractFontStyle(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();

        if (name.Contains("italic"))
            return "italic";
        if (name.Contains("oblique"))
            return "oblique";

        return "normal";
    }

    /// <summary>
    /// Возвращает формат шрифта для CSS
    /// </summary>
    private string GetFontFormat(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".woff2" => "woff2",
            ".woff" => "woff",
            ".ttf" => "truetype",
            ".otf" => "opentype",
            ".eot" => "embedded-opentype",
            ".svg" => "svg",
            _ => string.Empty
        };
    }

    /// <summary>
    /// Очищает имя файла от недопустимых символов
    /// </summary>
    private string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = fileName;

        foreach (var c in invalidChars)
        {
            sanitized = sanitized.Replace(c, '_');
        }

        return sanitized;
    }

    /// <summary>
    /// Генерирует CSS переменные из свойств контента
    /// </summary>
    private void GenerateCssVariables(IContent content, StringBuilder cssBuilder)
    {
        var propertyEditorAlias = new HashSet<string>
        {
            "Umbraco.TextBox",
            "Umbraco.ColorPicker.EyeDropper",
            "Umbraco.Integer"
        };

        foreach (var property in content.Properties)
        {
            if (!propertyEditorAlias.Contains(property.PropertyType.PropertyEditorAlias))
            {
                continue;
            }

            var propertyValue = property.GetValue();
            var cssValue = GenerateCssValue(property.PropertyType.PropertyEditorAlias, propertyValue);

            if (!string.IsNullOrEmpty(cssValue))
            {
                cssBuilder.AppendLine($"  --{property.Alias}: {cssValue};");
            }
        }
    }

    /// <summary>
    /// Генерирует CSS значение для свойства
    /// </summary>
    private string GenerateCssValue(string propertyEditorAlias, object? propertyValue)
    {
        if (propertyValue == null)
        {
            return null;
        }

        return propertyEditorAlias switch
        {
            "Umbraco.ColorPicker.EyeDropper" => propertyValue.ToString(),
            "Umbraco.Integer" => $"{propertyValue}px",
            _ => propertyValue.ToString()
        };
    }

    /// <summary>
    /// Вспомогательный класс для информации о файле шрифта
    /// </summary>
    private class FontFile
    {
        public string Path { get; set; } = string.Empty;
        public string Extension { get; set; } = string.Empty;
        public string Weight { get; set; } = "400";
        public string Style { get; set; } = "normal";
    }
}
