namespace UrlShortener.Api.Domain;

public class ShortenedUrl
{
    public long Id { get; set; }
    public string LongUrl { get; set; } = string.Empty;
    public string ShortCode { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long ClickCount { get; set; } = 0;

    // A shortened URL has many click event
    public ICollection<ClickEvent> Clicks { get; set; } = new List<ClickEvent>();
}