using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Data;
using UrlShortener.Api.Domain;

namespace UrlShortener.Api.Services;

public class ClickAnalyticsWorker : BackgroundService
{
    private readonly ClickChannel _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ClickAnalyticsWorker> _logger;

    public ClickAnalyticsWorker(
        ClickChannel channel,
        IServiceScopeFactory scopeFactory,
        ILogger<ClickAnalyticsWorker> logger)
    {
        _channel = channel;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<ClickEvent>();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await _channel.Reader.WaitToReadAsync(stoppingToken))
                {
                    while (_channel.Reader.TryRead(out var clickEvent))
                    {
                        batch.Add(clickEvent);
                        if (batch.Count >= 100) break;
                    }

                    if (batch.Count > 0)
                    {
                        await FlushBatchAsync(batch, stoppingToken);
                        batch.Clear();
                    }
                }

                await Task.Delay(1000, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed at processing click queue.");
            }
        }
    }

    private async Task FlushBatchAsync(List<ClickEvent> events, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.ClickEvents.AddRangeAsync(events, ct);
        await db.SaveChangesAsync(ct);

        var groupedCounts = events
            .GroupBy(e => e.ShortenedUrlId)
            .Select(g => new { UrlId = g.Key, Count = g.Count() });

        foreach (var item in groupedCounts)
        {
            await db.ShortenedUrls
                .Where(u => u.Id == item.UrlId)
                .ExecuteUpdateAsync(s => s.SetProperty(
                    u => u.ClickCount,
                    u => u.ClickCount + item.Count), ct);
        }
    }
}