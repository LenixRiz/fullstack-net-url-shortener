using Microsoft.EntityFrameworkCore;
using UrlShortener.Api.Domain;

namespace UrlShortener.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
        
    }

    // Set ShortenedUrl to become a table called ShortenedUrls
    public DbSet<ShortenedUrl> ShortenedUrls => Set<ShortenedUrl>();
    public DbSet<ClickEvent> ClickEvents => Set<ClickEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Fluent API configuration
        modelBuilder.Entity<ShortenedUrl>(entity =>
        {
            // Make sure ShortCode row is unique and has index so read path can be quick when searched, capped at 10 length
            entity.HasIndex(e => e.ShortCode)
                .IsUnique();
            entity.Property(e => e.ShortCode)
                .HasMaxLength(10)
                .IsRequired();

            // Make sure LongUrl is required
            entity.Property(e => e.LongUrl)
                .IsRequired();

            // Configure 1-to-Many relationshop  
            entity.HasMany(e => e.Clicks)
                .WithOne(c => c.ShortenedUrl)
                .HasForeignKey(c => c.ShortenedUrlId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ClickEvent>(entity =>
        {
           entity.HasKey(e => e.Id);

           entity.HasIndex(e => e.ShortenedUrlId);
           entity.HasIndex(e => e.ClickedAt);
        });
    }
}