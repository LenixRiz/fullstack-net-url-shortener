using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using UrlShortener.Api.Data;
using UrlShortener.Api.Domain;
using UrlShortener.Api.Services;
using UrlShortener.Shared.DTOs;

DotNetEnv.Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

var dbHost = Environment.GetEnvironmentVariable("DB_HOST")
    ?? throw new InvalidOperationException("DB_HOST missing or .env failed to load");

var connBuilder = new NpgsqlConnectionStringBuilder
{
    Host = Environment.GetEnvironmentVariable("DB_HOST"),
    Port = int.TryParse(Environment.GetEnvironmentVariable("DB_PORT"), out var port) ? port : 5433,
    Database = Environment.GetEnvironmentVariable("DB_NAME"),
    Username = Environment.GetEnvironmentVariable("DB_USER"),
    Password = Environment.GetEnvironmentVariable("DB_PASSWORD")
};

Console.WriteLine($"[DEBUG] DATABASE TARGET: {connBuilder.Database} DI PORT: {connBuilder.Port}");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connBuilder.ConnectionString));

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IIdGenerator>(_ => new SnowflakeIdGenerator(nodeId: 1));
builder.Services.AddSingleton<ClickChannel>();
builder.Services.AddHostedService<ClickAnalyticsWorker>();

var app = builder.Build();

app.MapPost("/api/shorten", async (
    ShortenUrlRequest request,
    AppDbContext dbContext,
    IIdGenerator idGenerator,
    LinkGenerator linkGenerator,
    HttpContext httpContext) =>
{
    if (!Uri.TryCreate(request.LongUrl, UriKind.Absolute, out var uriResult)
    || (uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps))
    {
        return Results.BadRequest(new { error = "Url format must start with http:// or https://"}); 
    }

    long id = idGenerator.CreateId();
    string shortCode = Base62Encoder.Encode(id);

    var shortenedUrl = new ShortenedUrl
    {
        Id = id,
        LongUrl = request.LongUrl,
        ShortCode = shortCode,
        CreatedAt = DateTime.UtcNow
    };

    dbContext.ShortenedUrls.Add(shortenedUrl);
    await dbContext.SaveChangesAsync();

    var fullShortUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/{shortCode}";

    return Results.Created($"/{shortCode}", new ShortenUrlResponse(shortCode, fullShortUrl, request.LongUrl));
});

app.MapGet("/{code}", async (
    string code,
    AppDbContext dbContext,
    IMemoryCache cache,
    ClickChannel clickChannel,
    HttpContext httpContext) =>
{
    if (!cache.TryGetValue(code, out ShortUrlCacheItem? cacheItem))
    {
        cacheItem = await dbContext.ShortenedUrls
        .AsNoTracking()
        .Where(u => u.ShortCode == code)
        .Select(u => new ShortUrlCacheItem(u.Id, u.LongUrl))
        .FirstOrDefaultAsync();

        if (cacheItem is null)
        {
            return Results.NotFound(new { message = "Link tidak ditemukan." });
        }

        var cacheOptions = new MemoryCacheEntryOptions()
            .SetSlidingExpiration(TimeSpan.FromHours(1))
            .SetAbsoluteExpiration(TimeSpan.FromDays(1));

        cache.Set(code, cacheItem, cacheOptions);
    }

    var clickEvent = new ClickEvent
    {
        Id = Guid.NewGuid(),
        ShortenedUrlId = cacheItem!.Id,
        ClickedAt = DateTime.UtcNow,
        UserAgent = httpContext.Request.Headers.UserAgent.ToString(),
        Referer = httpContext.Request.Headers.Referer.ToString()
    };

    clickChannel.Writer.TryWrite(clickEvent);

    return Results.Redirect(cacheItem.LongUrl, permanent: false);
});

app.MapGet("/api/stats/{code}", async (
    string code,
    AppDbContext dbContext) =>
{
    var url = await dbContext.ShortenedUrls
        .AsNoTracking()
        .Where(u => u.ShortCode == code)
        .Select(u => new
        {
            u.Id,
            u.ShortCode,
            u.LongUrl,
            u.CreatedAt,
            u.ClickCount
        })
        .FirstOrDefaultAsync();

    if (url is null)
    {
        return Results.NotFound(new { message = "Link not found" });
    }

    var sevenDaysAgo = DateTime.UtcNow.Date.AddDays(-7);

    var rawDailyClicks = await dbContext.ClickEvents
    .AsNoTracking()
    .Where(c => c.ShortenedUrlId == url.Id && c.ClickedAt >= sevenDaysAgo)
    .GroupBy(c => c.ClickedAt.Date)
    .Select(g => new
    {
        Date = g.Key,
        Count = g.Count()
    })
    .OrderBy(s => s.Date)
    .ToListAsync();

    var dailyClicks = rawDailyClicks
    .Select(x => new DailyClickDto(x.Date, x.Count))
    .ToList();

    var response = new UrlStatsResponse(
        url.ShortCode,
        url.LongUrl,
        url.CreatedAt,
        url.ClickCount,
        dailyClicks
    );

    return Results.Ok(response);
});

app.Run();