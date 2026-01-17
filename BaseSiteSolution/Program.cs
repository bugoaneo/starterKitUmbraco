using BaseSiteSolution.Infrastructure.Smidge;
using BaseSiteSolution.Infrastructure.Services;
using Microsoft.Extensions.Options;
using Smidge;
using Smidge.Cache;
using Smidge.Nuglify;
using Smidge.Options;
using Umbraco.Community.Smidge;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Настройка Smidge с учетом окружения
var isDevelopment = builder.Environment.IsDevelopment();

// Базовая регистрация Smidge
// Настройки берутся из appsettings.json и appsettings.Development.json
builder.Services.AddSmidge();

// Настройка CacheBuster: для Development используем TimestampCacheBuster (обновляется при каждом запросе),
// для Production - DynamicCacheBuster (можно обновлять программно при публикации themeEditor)
// Создаем через фабрику, чтобы получить ILogger
builder.Services.AddSingleton<DynamicCacheBuster>(serviceProvider =>
{
    var logger = serviceProvider.GetRequiredService<ILogger<DynamicCacheBuster>>();
    return new DynamicCacheBuster(logger);
});
builder.Services.AddSingleton<ICacheBuster>(serviceProvider =>
{
    if (isDevelopment)
    {
        return new TimestampCacheBuster(builder.Environment);
    }
    return serviceProvider.GetRequiredService<DynamicCacheBuster>();
});

// Подключение Nuglify для минификации (используется только в Production)
// В Development минификация отключена через appsettings.Development.json
if (!isDevelopment)
{
    builder.Services.AddSmidgeNuglify();
}

// Регистрация сервиса для очистки кеша Smidge
builder.Services.AddScoped<SmidgeCacheService>();

// Регистрация сервиса для генерации CSS с переменными стилей
builder.Services.AddScoped<ThemeCssGeneratorService>();

builder.CreateUmbracoBuilder()
    .AddBackOffice()
    .AddWebsite()
    .AddComposers()
    .AddRuntimeMinifier()
    .Build();

WebApplication app = builder.Build();

await app.BootUmbracoAsync();

app.UseUmbraco()
    .WithMiddleware(u =>
    {
        u.UseBackOffice();
        u.UseWebsite();
    })
    .WithEndpoints(u =>
    {
        u.UseBackOfficeEndpoints();
        u.UseWebsiteEndpoints();
    });

// Настройка Smidge ПОСЛЕ UseUmbraco
// Бандлы создаются автоматически из appsettings.json
app.UseSmidge();

await app.RunAsync();
