using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediaFetch.Api.Contracts;
using MediaFetch.Api.Data;
using MediaFetch.Api.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFetch.Tests.Integration;

public sealed class DownloadsApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    [Fact]
    public async Task Create_ThenDownloadFile_CompletesEndToEnd()
    {
        using var factory = new MediaFetchWebApplicationFactory();
        using var client = factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync(
            "/api/downloads",
            new CreateDownloadRequest(
                "https://example.com/video",
                DownloadMode.Video,
                720,
                null));

        Assert.True(
            createResponse.StatusCode == HttpStatusCode.Accepted,
            await createResponse.Content.ReadAsStringAsync());
        var created = await createResponse.Content.ReadFromJsonAsync<DownloadJobResponse>(JsonOptions);
        Assert.NotNull(created);

        DownloadJobResponse? current = null;
        for (var attempt = 0; attempt < 50; attempt++)
        {
            await Task.Delay(50);
            current = await client.GetFromJsonAsync<DownloadJobResponse>(
                $"/api/downloads/{created.Id}",
                JsonOptions);

            if (current?.Status == DownloadStatus.Completed)
            {
                break;
            }
        }

        Assert.Equal(DownloadStatus.Completed, current?.Status);

        var fileResponse = await client.GetAsync($"/api/downloads/{created.Id}/file");
        Assert.Equal(HttpStatusCode.OK, fileResponse.StatusCode);
        Assert.Equal([1, 2, 3, 4], await fileResponse.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Create_WithUnsupportedHeight_ReturnsBadRequest()
    {
        using var factory = new MediaFetchWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/downloads",
            new CreateDownloadRequest(
                "https://example.com/video",
                DownloadMode.Video,
                1440,
                null));

        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetFile_BeforeCompletion_ReturnsConflict()
    {
        using var factory = new MediaFetchWebApplicationFactory();
        using var client = factory.CreateClient();

        var job = DownloadJob.Create(
            "https://example.com/not-enqueued",
            DownloadMode.Video,
            720,
            null);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<MediaFetchDbContext>();
            dbContext.DownloadJobs.Add(job);
            await dbContext.SaveChangesAsync();
        }

        var response = await client.GetAsync($"/api/downloads/{job.Id}/file");

        Assert.True(
            response.StatusCode == HttpStatusCode.Conflict,
            await response.Content.ReadAsStringAsync());
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
