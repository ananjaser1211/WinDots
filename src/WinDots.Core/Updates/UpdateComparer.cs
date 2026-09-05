namespace WinDots.Core.Updates;

/// <summary>
/// Pure decision logic that turns a current version plus a fetched release into an <see cref="UpdateResult"/>.
/// Pre-releases are ignored unless the current version is itself a pre-release. Deterministic and side-effect free.
/// </summary>
public static class UpdateComparer
{
    public static UpdateResult Decide(SemanticVersion current, ReleaseInfo latest)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(latest);

        if (!SemanticVersion.TryParse(latest.TagName, out SemanticVersion? latestVersion))
        {
            return UpdateResult.Failed($"Unrecognised release tag '{latest.TagName}'.");
        }

        // Skip pre-releases unless the running build is itself a pre-release (opted into the pre-release channel).
        bool isPre = latest.IsPrerelease || latestVersion.IsPreRelease;
        if (isPre && !current.IsPreRelease)
        {
            return UpdateResult.UpToDate();
        }

        return latestVersion > current
            ? UpdateResult.Available(latestVersion, latest.HtmlUrl, latest.PublishedAt)
            : UpdateResult.UpToDate();
    }
}
