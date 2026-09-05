namespace WinDots.Core.Updates;

/// <summary>The outcome of an update check.</summary>
public enum UpdateStatus
{
    /// <summary>The running version is the latest applicable release.</summary>
    UpToDate,

    /// <summary>A newer applicable release exists.</summary>
    UpdateAvailable,

    /// <summary>The check could not complete (network, non-200, parse, or an unrecognised tag).</summary>
    Error,
}

/// <summary>
/// Raw information about a single GitHub release, produced by an <see cref="IReleaseSource"/> and consumed by the
/// pure comparison logic. Windows and HTTP types stay out of this record so Core remains BCL-only and testable.
/// </summary>
/// <param name="TagName">The release tag (e.g. <c>v0.2.0</c>).</param>
/// <param name="HtmlUrl">The human-facing release page URL.</param>
/// <param name="PublishedAt">When the release was published, or null.</param>
/// <param name="IsPrerelease">Whether GitHub marked the release as a pre-release.</param>
public sealed record ReleaseInfo(string TagName, string HtmlUrl, DateTimeOffset? PublishedAt, bool IsPrerelease);

/// <summary>
/// The result of fetching the latest release from an <see cref="IReleaseSource"/>. Exactly one of
/// <see cref="Release"/> or <see cref="Error"/> is set; a source translates every failure into
/// <see cref="Failed"/> rather than throwing.
/// </summary>
public sealed record ReleaseFetch(ReleaseInfo? Release, string? Error)
{
    public static ReleaseFetch Ok(ReleaseInfo release) => new(release, null);

    public static ReleaseFetch Failed(string error) => new(null, error);
}

/// <summary>
/// The result surfaced to the UI after an update check. <see cref="LatestVersion"/>, <see cref="ReleaseUrl"/> and
/// <see cref="PublishedAt"/> are populated only when a newer release is available; <see cref="Error"/> only when the
/// check failed.
/// </summary>
public sealed record UpdateResult(
    UpdateStatus Status,
    SemanticVersion? LatestVersion,
    string? ReleaseUrl,
    DateTimeOffset? PublishedAt,
    string? Error)
{
    public static UpdateResult UpToDate() => new(UpdateStatus.UpToDate, null, null, null, null);

    public static UpdateResult Available(SemanticVersion latest, string releaseUrl, DateTimeOffset? publishedAt) =>
        new(UpdateStatus.UpdateAvailable, latest, releaseUrl, publishedAt, null);

    public static UpdateResult Failed(string error) => new(UpdateStatus.Error, null, null, null, error);
}
