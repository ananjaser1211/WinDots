using WinDots.Core.Updates;

namespace WinDots.Core.Tests.Updates;

public sealed class SemanticVersionTests
{
    [Theory]
    [InlineData("0.2.0", 0, 2, 0, null)]
    [InlineData("v0.2.0", 0, 2, 0, null)]
    [InlineData("V1.0.0", 1, 0, 0, null)]
    [InlineData("1.0.0-beta.1", 1, 0, 0, "beta.1")]
    [InlineData("v2.3.4-rc.2+build.99", 2, 3, 4, "rc.2")]
    [InlineData("1", 1, 0, 0, null)]
    [InlineData("1.2", 1, 2, 0, null)]
    [InlineData("0.1.0.0", 0, 1, 0, null)]
    [InlineData("  1.2.3  ", 1, 2, 3, null)]
    public void TryParse_AcceptsWellFormedTags(string text, int major, int minor, int patch, string? pre)
    {
        Assert.True(SemanticVersion.TryParse(text, out SemanticVersion? version));
        Assert.Equal(major, version!.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(patch, version.Patch);
        Assert.Equal(pre, version.PreRelease);
        Assert.Equal(pre is not null, version.IsPreRelease);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("v")]
    [InlineData("abc")]
    [InlineData("1.2.x")]
    [InlineData("1.-2.3")]
    [InlineData("1..3")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-beta..1")]
    [InlineData("1.2.3.4.5")]
    [InlineData("latest")]
    public void TryParse_RejectsJunk(string? text)
    {
        Assert.False(SemanticVersion.TryParse(text, out SemanticVersion? version));
        Assert.Null(version);
    }

    [Fact]
    public void Parse_ThrowsOnJunk()
    {
        Assert.Throws<FormatException>(() => SemanticVersion.Parse("not-a-version"));
    }

    [Fact]
    public void VPrefix_IsToleratedAndEqualToUnprefixed()
    {
        Assert.Equal(SemanticVersion.Parse("1.2.3"), SemanticVersion.Parse("v1.2.3"));
    }

    [Fact]
    public void Ordering_ByCoreComponents()
    {
        Assert.True(SemanticVersion.Parse("1.0.0") < SemanticVersion.Parse("1.0.1"));
        Assert.True(SemanticVersion.Parse("1.0.1") < SemanticVersion.Parse("1.1.0"));
        Assert.True(SemanticVersion.Parse("1.9.9") < SemanticVersion.Parse("2.0.0"));
        Assert.True(SemanticVersion.Parse("2.0.0") > SemanticVersion.Parse("1.99.99"));
    }

    [Fact]
    public void PreRelease_IsLowerThanRelease()
    {
        Assert.True(SemanticVersion.Parse("1.0.0-beta.1") < SemanticVersion.Parse("1.0.0"));
        Assert.True(SemanticVersion.Parse("1.0.0") > SemanticVersion.Parse("1.0.0-rc.1"));
    }

    [Fact]
    public void PreRelease_Ordering_FollowsSemver()
    {
        // Numeric identifiers compare numerically; fewer identifiers rank lower; numeric < alphanumeric.
        Assert.True(SemanticVersion.Parse("1.0.0-alpha") < SemanticVersion.Parse("1.0.0-alpha.1"));
        Assert.True(SemanticVersion.Parse("1.0.0-alpha.1") < SemanticVersion.Parse("1.0.0-alpha.2"));
        Assert.True(SemanticVersion.Parse("1.0.0-alpha.2") < SemanticVersion.Parse("1.0.0-alpha.10"));
        Assert.True(SemanticVersion.Parse("1.0.0-alpha") < SemanticVersion.Parse("1.0.0-beta"));
        Assert.True(SemanticVersion.Parse("1.0.0-1") < SemanticVersion.Parse("1.0.0-alpha"));
    }

    [Fact]
    public void Equality_IgnoresBuildMetadata_AndVPrefix()
    {
        SemanticVersion a = SemanticVersion.Parse("1.2.3+build.1");
        SemanticVersion b = SemanticVersion.Parse("v1.2.3+build.2");
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.True(a == b);
        Assert.False(a != b);
    }

    [Fact]
    public void Equality_DistinguishesPreRelease()
    {
        Assert.NotEqual(SemanticVersion.Parse("1.0.0"), SemanticVersion.Parse("1.0.0-rc.1"));
    }

    [Fact]
    public void ToString_RoundTripsThroughParse()
    {
        Assert.Equal("1.2.3", SemanticVersion.Parse("v1.2.3").ToString());
        Assert.Equal("1.2.3-beta.1", SemanticVersion.Parse("1.2.3-beta.1+meta").ToString());
    }

    [Fact]
    public void FourthComponent_IsIgnored()
    {
        Assert.Equal(SemanticVersion.Parse("0.1.0"), SemanticVersion.Parse("0.1.0.0"));
        Assert.Equal(SemanticVersion.Parse("0.1.0"), SemanticVersion.Parse("0.1.0.7"));
    }

    [Fact]
    public void CompareTo_Null_SortsFirst()
    {
        Assert.True(SemanticVersion.Parse("1.0.0").CompareTo(null) > 0);
    }
}
