using System.Diagnostics.CodeAnalysis;
using LageBuch.AppLogic.Services;

namespace LageBuch.AppLogic.Tests;

[SuppressMessage("Design", "CA1054", Justification = "Tests exercise the validator with free-form (even hostile) strings — that is the point of these tests.")]
public class HttpUrlValidatorTests
{
    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://example.com")]
    [InlineData("https://example.com/path?query=1#fragment")]
    public void TryGetHttpUri_accepts_a_well_formed_http_or_https_url(string url)
    {
        Assert.True(HttpUrlValidator.TryGetHttpUri(url, out var uri));
        Assert.Equal(url, uri.AbsoluteUri.TrimEnd('/'));
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.org")]
    [InlineData("intent://example.com#Intent;scheme=https;end")]
    [InlineData("content://media/external/images/1")]
    public void TryGetHttpUri_refuses_a_non_http_scheme(string url)
    {
        Assert.False(HttpUrlValidator.TryGetHttpUri(url, out _));
    }

    // Normalization (turning a bare domain into https://<domain>) is a caller concern, e.g.
    // LinksViewModel.OpenAsync — the validator itself only accepts already-absolute URLs.
    [Fact]
    public void TryGetHttpUri_refuses_a_bare_domain_without_a_scheme()
    {
        Assert.False(HttpUrlValidator.TryGetHttpUri("example.com", out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryGetHttpUri_refuses_null_empty_or_whitespace(string? url)
    {
        Assert.False(HttpUrlValidator.TryGetHttpUri(url, out _));
    }
}
