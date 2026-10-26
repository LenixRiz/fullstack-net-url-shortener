namespace UrlShortener.Api.Domain;

public class ClickEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long ShortenedUrlId { get; set; }
    public DateTime ClickedAt { get; set; } = DateTime.UtcNow;
    public string? UserAgent { get; set; }
    public string? Referer { get; set; }
    
    public ShortenedUrl? ShortenedUrl { get; set; }
}