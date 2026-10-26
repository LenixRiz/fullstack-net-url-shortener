using Microsoft.EntityFrameworkCore;
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
    Port = int.TryParse(Environment.GetEnvironmentVariable("DB_PORT"), out var port) ? port : 5432,
    Database = Environment.GetEnvironmentVariable("DB_NAME"),
    Username = Environment.GetEnvironmentVariable("DB_USER"),
    Password = Environment.GetEnvironmentVariable("DB_PASSWORD")
};

Console.WriteLine($"[DEBUG] DATABASE TARGET: {connBuilder.Database} DI PORT: {connBuilder.Port}");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connBuilder.ConnectionString));

builder.Services.AddSingleton<IIdGenerator>(_ => new SnowflakeIdGenerator(nodeId: 1));

var app = builder.Build();

app.MapPost("/api/shorten", async (
    ShortenUrlRequest request,
    AppDbContext dbContext,
    IIdGenerator idGenerator,
    LinkGenerator linkGenerator,
    HttpContext httpContext) =>
{
    if (!Uri.TryCreate(request.LongUrl, UriKind.Absolute, out var uriResult)
    || uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps)
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

app.Run();