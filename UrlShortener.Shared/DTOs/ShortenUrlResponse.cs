namespace UrlShortener.Shared.DTOs;

public record ShortenUrlResponse
(
    string ShortUrl,
    string ShortCode,
    string OriginalUrl
);