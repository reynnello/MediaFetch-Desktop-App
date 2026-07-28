using MediaFetch.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace MediaFetch.Api.Data;

public sealed class MediaFetchDbContext(DbContextOptions<MediaFetchDbContext> options)
    : DbContext(options)
{
    public DbSet<DownloadJob> DownloadJobs => Set<DownloadJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var job = modelBuilder.Entity<DownloadJob>();

        job.ToTable("DownloadJobs");
        job.HasKey(x => x.Id);
        job.Property(x => x.SourceUrl).HasMaxLength(2048).IsRequired();
        job.Property(x => x.Mode).HasConversion<string>().HasMaxLength(16);
        job.Property(x => x.AudioFormat).HasConversion<string>().HasMaxLength(16);
        job.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        job.Property(x => x.Title).HasMaxLength(500);
        job.Property(x => x.Author).HasMaxLength(300);
        job.Property(x => x.Source).HasMaxLength(100);
        job.Property(x => x.ThumbnailUrl).HasMaxLength(2048);
        job.Property(x => x.OutputPath).HasMaxLength(2048);
        job.Property(x => x.Error).HasMaxLength(4000);
        job.HasIndex(x => x.Status);
        job.HasIndex(x => x.CreatedAt);
    }
}
