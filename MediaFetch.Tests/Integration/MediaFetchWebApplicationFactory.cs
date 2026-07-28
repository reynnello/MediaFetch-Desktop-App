using MediaFetch.Api.Domain;
using MediaFetch.Api.Data;
using MediaFetch.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace MediaFetch.Tests.Integration;

public sealed class MediaFetchWebApplicationFactory : WebApplicationFactory<Program>
{
    public string TestRoot { get; } =
        Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(TestRoot);
        var outputDirectory = Path.Combine(TestRoot, "downloads");

        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MediaFetch:OutputDirectory"] = outputDirectory
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<MediaFetchDbContext>>();
            services.RemoveAll<IMediaUrlValidator>();
            services.RemoveAll<IYtDlpRunner>();

            var databaseName = $"mediafetch-{Guid.NewGuid():N}";
            var connectionString = $"Data Source={databaseName};Mode=Memory;Cache=Shared";
            var anchorConnection = new SqliteConnection(connectionString);
            anchorConnection.Open();
            services.AddSingleton(anchorConnection);
            services.AddDbContext<MediaFetchDbContext>(options =>
                options.UseSqlite(connectionString));
            services.AddSingleton<IMediaUrlValidator, AlwaysValidUrlValidator>();
            services.AddSingleton<IYtDlpRunner>(new FakeYtDlpRunner(outputDirectory));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && Directory.Exists(TestRoot))
        {
            Directory.Delete(TestRoot, recursive: true);
        }
    }

    private sealed class AlwaysValidUrlValidator : IMediaUrlValidator
    {
        public Task<UrlValidationResult> ValidateAsync(
            string url,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                Uri.TryCreate(url, UriKind.Absolute, out var uri)
                    ? UrlValidationResult.Valid(uri)
                    : UrlValidationResult.Invalid("Invalid URL."));
    }

    private sealed class FakeYtDlpRunner(string outputDirectory) : IYtDlpRunner
    {
        public Task<MediaMetadata> InspectAsync(Uri url, CancellationToken cancellationToken) =>
            Task.FromResult(new MediaMetadata(
                "Integration test video",
                "Test runner",
                TimeSpan.FromSeconds(10),
                "Fake",
                null,
                url.AbsoluteUri));

        public async Task<string> DownloadAsync(
            DownloadJob job,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(outputDirectory);
            progress.Report(new DownloadProgress(50, 2, 4));
            var path = Path.Combine(outputDirectory, $"{job.Id:N}.mp4");
            await File.WriteAllBytesAsync(path, [1, 2, 3, 4], cancellationToken);
            progress.Report(new DownloadProgress(100, 4, 4));
            return path;
        }
    }
}
