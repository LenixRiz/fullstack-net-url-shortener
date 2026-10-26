using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Data;
using UrlShortener.Api.Domain;
using UrlShortener.Api.Services;
using UrlShortener.Shared.DTOs;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

var app = builder.Build();

app.MapPost("/api/shorten", async (
    ShortenUrlRequest request,
    AppDbContext dbContext,
    IIdGenerator idGenerator,
    LinkGenerator linkGenerator,
    HttpContext httpContext) =>
{
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