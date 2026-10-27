namespace UrlShortener.Shared.DTOs;

public record DailyClickDto(DateTime Date, int Count);

public record UrlStatsResponse(
    string ShortCode,
    string LongUrl,
    DateTime CreatedAt,
    long TotalClicks,
    List<DailyClickDto> DailyClicks
);