using System.ComponentModel.DataAnnotations;
using MediaFetch.Api.Domain;

namespace MediaFetch.Api.Contracts;

public sealed record CreateDownloadRequest(
    [Required, StringLength(2048)] string Url,
    DownloadMode Mode = DownloadMode.Video,
    int? MaxHeight = null,
    AudioFormat? AudioFormat = null);
