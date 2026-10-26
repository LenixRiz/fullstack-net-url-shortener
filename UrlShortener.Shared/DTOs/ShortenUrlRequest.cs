using System.ComponentModel.DataAnnotations;

namespace UrlShortener.Shared.DTOs;

public record ShortenUrlRequest
(
    [property: Required]
    [property: Url]
    string LongUrl
);