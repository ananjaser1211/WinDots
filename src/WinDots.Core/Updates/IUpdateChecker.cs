namespace WinDots.Core.Updates;

/// <summary>
/// Checks whether a newer release than <paramref name="currentVersion"/> is available. The signature carries no
/// Windows or HTTP types; the concrete <see cref="UpdateChecker"/> fetches release data through an injected
/// <see cref="IReleaseSource"/>. Never throws for expected failures — a failed fetch becomes an
/// <see cref="UpdateStatus.Error"/> result.
/// </summary>
public interface IUpdateChecker
{
    Task<UpdateResult> CheckAsync(SemanticVersion currentVersion, CancellationToken ct);
}
