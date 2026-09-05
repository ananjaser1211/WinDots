using WinDots.Core.Updates;

namespace WinDots.Core.Tests.Updates;

public sealed class UpdateCheckerTests
{
    private sealed class FakeReleaseSource : IReleaseSource
    {
        private readonly Func<CancellationToken, Task<ReleaseFetch>> _responder;

        public FakeReleaseSource(ReleaseFetch fetch) => _responder = _ => Task.FromResult(fetch);

        public FakeReleaseSource(Func<CancellationToken, Task<ReleaseFetch>> responder) => _responder = responder;

        public Task<ReleaseFetch> GetLatestReleaseAsync(CancellationToken ct) => _responder(ct);
    }

    private static ReleaseInfo Release(string tag, bool prerelease = false) =>
        new(tag, $"https://github.com/AnanJaser1211/WinDots/releases/tag/{tag}", DateTimeOffset.UnixEpoch, prerelease);

    [Fact]
    public async Task UpToDate_WhenLatestEqualsCurrent()
    {
        var checker = new UpdateChecker(new FakeReleaseSource(ReleaseFetch.Ok(Release("v1.0.0"))));

        UpdateResult result = await checker.CheckAsync(SemanticVersion.Parse("1.0.0"), CancellationToken.None);

        Assert.Equal(UpdateStatus.UpToDate, result.Status);
    }

    [Fact]
    public async Task UpdateAvailable_WhenLatestNewer()
    {
        var checker = new UpdateChecker(new FakeReleaseSource(ReleaseFetch.Ok(Release("v0.3.0"))));

        UpdateResult result = await checker.CheckAsync(SemanticVersion.Parse("0.1.0"), CancellationToken.None);

        Assert.Equal(UpdateStatus.UpdateAvailable, result.Status);
        Assert.Equal(SemanticVersion.Parse("0.3.0"), result.LatestVersion);
        Assert.Equal("https://github.com/AnanJaser1211/WinDots/releases/tag/v0.3.0", result.ReleaseUrl);
    }

    [Fact]
    public async Task PreRelease_Skipped_WhenCurrentStable()
    {
        var checker = new UpdateChecker(new FakeReleaseSource(ReleaseFetch.Ok(Release("v0.3.0-beta.1", prerelease: true))));

        UpdateResult result = await checker.CheckAsync(SemanticVersion.Parse("0.1.0"), CancellationToken.None);

        Assert.Equal(UpdateStatus.UpToDate, result.Status);
    }

    [Fact]
    public async Task NetworkError_IsSurfacedAsError()
    {
        var checker = new UpdateChecker(new FakeReleaseSource(ReleaseFetch.Failed("Could not reach GitHub.")));

        UpdateResult result = await checker.CheckAsync(SemanticVersion.Parse("0.1.0"), CancellationToken.None);

        Assert.Equal(UpdateStatus.Error, result.Status);
        Assert.Equal("Could not reach GitHub.", result.Error);
    }

    [Fact]
    public async Task ThrownException_IsCaughtAsError()
    {
        var checker = new UpdateChecker(new FakeReleaseSource(
            _ => throw new InvalidOperationException("boom")));

        UpdateResult result = await checker.CheckAsync(SemanticVersion.Parse("0.1.0"), CancellationToken.None);

        Assert.Equal(UpdateStatus.Error, result.Status);
        Assert.Equal("boom", result.Error);
    }

    [Fact]
    public async Task Cancellation_IsPropagated()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var checker = new UpdateChecker(new FakeReleaseSource(
            ct => Task.FromCanceled<ReleaseFetch>(ct)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => checker.CheckAsync(SemanticVersion.Parse("0.1.0"), cts.Token));
    }
}
