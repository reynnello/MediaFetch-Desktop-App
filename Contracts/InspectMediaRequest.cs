using System.ComponentModel.DataAnnotations;

namespace MediaFetch.Api.Contracts;

public sealed record InspectMediaRequest(
    [Required, StringLength(2048)] string Url);
