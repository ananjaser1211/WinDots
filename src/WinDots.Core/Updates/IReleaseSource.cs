namespace WinDots.Core.Updates;

/// <summary>
/// Fetches the latest published release for the project. The concrete implementation lives in the Windows layer
/// (an HTTP GET against the GitHub REST API); Core depends only on this abstraction so the update logic stays
/// BCL-only and unit-testable with a fake. Implementations never throw for expected failures: a network error,
/// a non-200 response, or a parse failure is returned as <see cref="ReleaseFetch.Failed"/>.
/// </summary>
public interface IReleaseSource
{
    Task<ReleaseFetch> GetLatestReleaseAsync(CancellationToken ct);
}
