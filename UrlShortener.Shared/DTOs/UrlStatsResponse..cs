namespace UrlShortener.Shared.DTOs;

public record UrlStatsResponse
(
    string ShortCode,
    string OriginalUrl,
    string CreatedAt,
    int TotalClicks
);