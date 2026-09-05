using WinDots.Core.Updates;

namespace WinDots.Core.Tests.Updates;

public sealed class UpdateComparerTests
{
    private static ReleaseInfo Release(string tag, bool prerelease = false) =>
        new(tag, $"https://github.com/AnanJaser1211/WinDots/releases/tag/{tag}", DateTimeOffset.UnixEpoch, prerelease);

    [Fact]
    public void NewerRelease_IsUpdateAvailable()
    {
        UpdateResult result = UpdateComparer.Decide(SemanticVersion.Parse("0.1.0"), Release("v0.2.0"));

        Assert.Equal(UpdateStatus.UpdateAvailable, result.Status);
        Assert.Equal(SemanticVersion.Parse("0.2.0"), result.LatestVersion);
        Assert.Equal("https://github.com/AnanJaser1211/WinDots/releases/tag/v0.2.0", result.ReleaseUrl);
        Assert.Equal(DateTimeOffset.UnixEpoch, result.PublishedAt);
        Assert.Null(result.Error);
    }

    [Fact]
    public void SameVersion_IsUpToDate()
    {
        UpdateResult result = UpdateComparer.Decide(SemanticVersion.Parse("1.0.0"), Release("v1.0.0"));
        Assert.Equal(UpdateStatus.UpToDate, result.Status);
    }

    [Fact]
    public void OlderRelease_IsUpToDate()
    {
        UpdateResult result = UpdateComparer.Decide(SemanticVersion.Parse("1.2.0"), Release("v1.1.0"));
        Assert.Equal(UpdateStatus.UpToDate, result.Status);
    }

    [Fact]
    public void NewerPreRelease_IsSkipped_WhenCurrentIsStable()
    {
        UpdateResult result = UpdateComparer.Decide(SemanticVersion.Parse("1.0.0"), Release("v1.1.0-beta.1", prerelease: true));
        Assert.Equal(UpdateStatus.UpToDate, result.Status);
    }

    [Fact]
    public void NewerPreRelease_IsOffered_WhenCurrentIsPreRelease()
    {
        UpdateResult result = UpdateComparer.Decide(SemanticVersion.Parse("1.1.0-beta.1"), Release("v1.1.0-beta.2", prerelease: true));
        Assert.Equal(UpdateStatus.UpdateAvailable, result.Status);
        Assert.Equal(SemanticVersion.Parse("1.1.0-beta.2"), result.LatestVersion);
    }

    [Fact]
    public void PreReleaseFlagFromTagAlone_IsSkipped()
    {
        // GitHub flag says false, but the tag itself carries a pre-release identifier.
        UpdateResult result = UpdateComparer.Decide(SemanticVersion.Parse("1.0.0"), Release("v1.1.0-rc.1", prerelease: false));
        Assert.Equal(UpdateStatus.UpToDate, result.Status);
    }

    [Fact]
    public void StableRelease_OffersUpgradeFromPreRelease()
    {
        UpdateResult result = UpdateComparer.Decide(SemanticVersion.Parse("1.0.0-rc.1"), Release("v1.0.0"));
        Assert.Equal(UpdateStatus.UpdateAvailable, result.Status);
    }

    [Fact]
    public void UnrecognisedTag_IsError()
    {
        UpdateResult result = UpdateComparer.Decide(SemanticVersion.Parse("1.0.0"), Release("nightly-latest"));
        Assert.Equal(UpdateStatus.Error, result.Status);
        Assert.NotNull(result.Error);
    }
}
