using FluentAssertions;

namespace Feuerwehr.Domain.Tests;

public class SmokeTest
{
    [Fact]
    public void Toolchain_is_wired()
    {
        true.Should().BeTrue();
    }
}
