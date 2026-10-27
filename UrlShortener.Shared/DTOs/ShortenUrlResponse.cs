namespace UrlShortener.Shared.DTOs;

public record ShortenUrlResponse
(
    string ShortCode,
    string ShortUrl,
    string OriginalUrl
);