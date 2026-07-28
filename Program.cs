using System.Text.Json.Serialization;
using MediaFetch.Api.Data;
using MediaFetch.Api.Infrastructure;
using MediaFetch.Api.Options;
using MediaFetch.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls("http://127.0.0.1:5080");

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

builder.Services
    .AddOptions<MediaFetchOptions>()
    .Bind(builder.Configuration.GetSection(MediaFetchOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

var connectionString = builder.Configuration.GetConnectionString("MediaFetch")
    ?? "Data Source=.mediafetch-data/mediafetch.db";

builder.Services.AddDbContext<MediaFetchDbContext>(options =>
    options.UseSqlite(connectionString));

builder.Services.AddSingleton<IDownloadQueue, DownloadQueue>();
builder.Services.AddSingleton<DownloadCancellationRegistry>();
builder.Services.AddSingleton<IMediaUrlValidator, MediaUrlValidator>();
builder.Services.AddSingleton<IYtDlpRunner, YtDlpRunner>();
builder.Services.AddHostedService<DownloadWorker>();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapGet("/", () => Results.Redirect("/swagger"));

await InitializeStorageAsync(app);
await app.RunAsync();

static async Task InitializeStorageAsync(WebApplication app)
{
    await using var scope = app.Services.CreateAsyncScope();
    var options = scope.ServiceProvider.GetRequiredService<IOptions<MediaFetchOptions>>().Value;
    var environment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();

    var outputDirectory = Path.GetFullPath(options.OutputDirectory, environment.ContentRootPath);
    var databaseDirectory = Path.GetDirectoryName(
        scope.ServiceProvider.GetRequiredService<MediaFetchDbContext>().Database.GetDbConnection().DataSource);

    Directory.CreateDirectory(outputDirectory);

    if (!string.IsNullOrWhiteSpace(databaseDirectory))
    {
        Directory.CreateDirectory(Path.GetFullPath(databaseDirectory, environment.ContentRootPath));
    }

    var dbContext = scope.ServiceProvider.GetRequiredService<MediaFetchDbContext>();
    await dbContext.Database.MigrateAsync();
}

public partial class Program;
