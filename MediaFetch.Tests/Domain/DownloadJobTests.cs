using MediaFetch.Api.Domain;

namespace MediaFetch.Tests.Domain;

public sealed class DownloadJobTests
{
    [Fact]
    public void Create_AppliesVideoDefaults()
    {
        var job = DownloadJob.Create(
            "https://example.com/video",
            DownloadMode.Video,
            maxHeight: null,
            audioFormat: null);

        Assert.Equal(DownloadStatus.Queued, job.Status);
        Assert.Equal(720, job.MaxHeight);
        Assert.Null(job.AudioFormat);
    }

    [Fact]
    public void Complete_FollowsValidStateTransitions()
    {
        var job = DownloadJob.Create(
            "https://example.com/video",
            DownloadMode.Audio,
            maxHeight: null,
            audioFormat: null);

        job.StartInspecting();
        job.StartDownloading(new MediaMetadata(
            "A title",
            "An author",
            TimeSpan.FromSeconds(42),
            "Test",
            null,
            null));
        job.UpdateProgress(55);
        job.Complete("output.mp3");

        Assert.Equal(DownloadStatus.Completed, job.Status);
        Assert.Equal(100, job.ProgressPercent);
        Assert.Equal(AudioFormat.Mp3, job.AudioFormat);
        Assert.Equal(42, job.DurationSeconds);
    }

    [Fact]
    public void InvalidTransition_Throws()
    {
        var job = DownloadJob.Create(
            "https://example.com/video",
            DownloadMode.Video,
            360,
            null);

        Assert.Throws<InvalidOperationException>(() => job.Complete("output.mp4"));
    }
}
