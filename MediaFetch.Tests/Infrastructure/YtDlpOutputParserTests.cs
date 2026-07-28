using MediaFetch.Api.Infrastructure;

namespace MediaFetch.Tests.Infrastructure;

public sealed class YtDlpOutputParserTests
{
    [Fact]
    public void ParseMetadata_ReadsExpectedFields()
    {
        const string json = """
            {
              "title": "Demo",
              "uploader": "Teacher",
              "duration": 12.5,
              "extractor_key": "Youtube",
              "thumbnail": "https://example.com/thumbnail.jpg",
              "webpage_url": "https://www.youtube.com/watch?v=demo"
            }
            """;

        var metadata = YtDlpOutputParser.ParseMetadata(json);

        Assert.Equal("Demo", metadata.Title);
        Assert.Equal("Teacher", metadata.Author);
        Assert.Equal(12.5, metadata.Duration?.TotalSeconds);
        Assert.Equal("Youtube", metadata.Source);
    }

    [Theory]
    [InlineData("download: 42.6%|1024|2048", 43, 1024L, 2048L)]
    [InlineData("[download] download:100.0%|2048|NA", 100, 2048L, null)]
    public void TryParseProgress_ReadsTemplate(
        string line,
        int expectedPercent,
        long expectedDownloaded,
        long? expectedTotal)
    {
        var parsed = YtDlpOutputParser.TryParseProgress(line, out var progress);

        Assert.True(parsed);
        Assert.Equal(expectedPercent, progress.Percent);
        Assert.Equal(expectedDownloaded, progress.DownloadedBytes);
        Assert.Equal(expectedTotal, progress.TotalBytes);
    }
}
