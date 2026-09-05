namespace WinDots.Core.Updates;

/// <summary>
/// The default <see cref="IUpdateChecker"/>: fetches the latest release via an injected <see cref="IReleaseSource"/>
/// and applies <see cref="UpdateComparer"/>. Any fetch failure (reported or thrown) is translated into an
/// <see cref="UpdateStatus.Error"/> result; genuine caller cancellation is propagated.
/// </summary>
public sealed class UpdateChecker : IUpdateChecker
{
    private readonly IReleaseSource _source;

    public UpdateChecker(IReleaseSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
    }

    public async Task<UpdateResult> CheckAsync(SemanticVersion currentVersion, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);

        try
        {
            ReleaseFetch fetch = await _source.GetLatestReleaseAsync(ct).ConfigureAwait(false);
            if (fetch.Error is not null || fetch.Release is null)
            {
                return UpdateResult.Failed(fetch.Error ?? "No release information was returned.");
            }

            return UpdateComparer.Decide(currentVersion, fetch.Release);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return UpdateResult.Failed(ex.Message);
        }
    }
}
