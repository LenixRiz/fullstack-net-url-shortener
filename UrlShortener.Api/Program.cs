using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Npgsql;
using UrlShortener.Api.Data;
using UrlShortener.Api.Domain;
using UrlShortener.Api.Services;
using UrlShortener.Shared.DTOs;
using StackExchange.Redis;

DotNetEnv.Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IIdGenerator>(_ => new SnowflakeIdGenerator(nodeId: 1));
builder.Services.AddSingleton<ClickChannel>();
builder.Services.AddHostedService<ClickAnalyticsWorker>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync( new
        {
            error = "Too many request. Please wait a few seconds before trying again."
        }, cancellationToken: token);
    };

    options.AddPolicy(policyName: "IpRateLimit", httpContext =>
    {
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(clientIp, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromSeconds(30),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazorClient", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

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

var redisConnection = Environment.GetEnvironmentVariable("REDIS_CONNECTION") ?? "localhost:6379";

Console.WriteLine($"[DEBUG] DATABASE TARGET: {connBuilder.Database} DI PORT: {connBuilder.Port}");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connBuilder.ConnectionString));

builder.Services.AddSingleton<IConnectionMultiplexer>(_ => 
    ConnectionMultiplexer.Connect(redisConnection));

var app = builder.Build();

const string BlazorCorsPolicy = "AllowBlazorClient";

app.UseCors(BlazorCorsPolicy);
app.UseRateLimiter();

app.MapPost("/api/shorten", async (
    ShortenUrlRequest request,
    AppDbContext dbContext,
    IIdGenerator idGenerator,
    IConnectionMultiplexer redis,
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

    var cacheDb = redis.GetDatabase();
    var payloadToCache = $"{id}:{request.LongUrl}";
    await cacheDb.StringSetAsync(shortCode, payloadToCache, TimeSpan.FromDays(1));

    var fullShortUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/{shortCode}";

    return Results.Created($"/{shortCode}", new ShortenUrlResponse(shortCode, fullShortUrl, request.LongUrl));
})
.RequireRateLimiting("IpRateLimit");

app.MapGet("/{code}", async (
    string code,
    AppDbContext dbContext,
    IConnectionMultiplexer redis,
    ClickChannel clickChannel,
    HttpContext httpContext) =>
{
    var cacheDb = redis.GetDatabase();
    
    string? cachedTarget = await cacheDb.StringGetAsync(code);
    long urlId;
    string targetLongUrl;

    if (!string.IsNullOrEmpty(cachedTarget))
    {
        var delimiterIndex = cachedTarget.IndexOf(':');
        urlId = long.Parse(cachedTarget[..delimiterIndex]);
        targetLongUrl = cachedTarget[(delimiterIndex + 1)..];
    }
    else
    {
        var urlEntity = await dbContext.ShortenedUrls
            .AsNoTracking()
            .Where(u => u.ShortCode == code)
            .Select(u => new { u.Id, u.LongUrl })
            .FirstOrDefaultAsync();

        if (urlEntity is null)
        {
            return Results.NotFound(new { message = "Url not found." });
        }

        urlId = urlEntity.Id;
        targetLongUrl = urlEntity.LongUrl;

        var payloadToCache = $"{urlId}:{targetLongUrl}";
        await cacheDb.StringSetAsync(code, payloadToCache, TimeSpan.FromDays(1));
    }

    var clickEvent = new ClickEvent
    {
        Id = Guid.NewGuid(),
        ShortenedUrlId = urlId,
        ClickedAt = DateTime.UtcNow,
        UserAgent = httpContext.Request.Headers.UserAgent.ToString(),
        Referer = httpContext.Request.Headers.Referer.ToString()
    };

    clickChannel.Writer.TryWrite(clickEvent);

    return Results.Redirect(targetLongUrl, permanent: false);
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