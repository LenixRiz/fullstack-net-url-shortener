using System.ComponentModel.DataAnnotations;

namespace UrlShortener.Shared.DTOs;

public record ShortenUrlRequest
{
    [Required(ErrorMessage = "URL cannot be empty.")]
    [Url(ErrorMessage = "Invalid URL format, make sure it has http:// or https://")]
    public string LongUrl { get; init; } = string.Empty;
}