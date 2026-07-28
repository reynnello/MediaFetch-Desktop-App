using System.Net;
using MediaFetch.Api.Services;

namespace MediaFetch.Tests.Services;

public sealed class MediaUrlValidatorTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.1")]
    [InlineData("172.16.0.1")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.1.1")]
    [InlineData("::1")]
    public void IsNonPublicAddress_BlocksPrivateAndLocalRanges(string text)
    {
        Assert.True(MediaUrlValidator.IsNonPublicAddress(IPAddress.Parse(text)));
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("2606:4700:4700::1111")]
    public void IsNonPublicAddress_AllowsPublicRanges(string text)
    {
        Assert.False(MediaUrlValidator.IsNonPublicAddress(IPAddress.Parse(text)));
    }

    [Fact]
    public async Task ValidateAsync_RejectsEmbeddedCredentialsWithoutDnsLookup()
    {
        var validator = new MediaUrlValidator();

        var result = await validator.ValidateAsync(
            "https://user:password@example.com/video",
            CancellationToken.None);

        Assert.False(result.IsValid);
    }
}
