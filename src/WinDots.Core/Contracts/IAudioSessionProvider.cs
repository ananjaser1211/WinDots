namespace WinDots.Core.Contracts;

public enum AudioMatchConfidence
{
    None,
    Medium,
    High,
}

/// <summary>Result of mapping a media session's source application to Core Audio sessions.</summary>
public sealed record AudioMatch(AudioMatchConfidence Confidence, IReadOnlyList<string> AudioSessionIds, string Explanation)
{
    public static AudioMatch NoMatch(string explanation) => new(AudioMatchConfidence.None, Array.Empty<string>(), explanation);
}

/// <summary>Application audio volume via Core Audio. Never targets master volume. Implemented in Milestone 5.</summary>
public interface IAudioSessionProvider
{
    Task<AudioMatch> MatchAsync(string sourceAppId, CancellationToken ct);

    Task<float?> GetVolumeAsync(AudioMatch match, CancellationToken ct);

    Task<bool?> GetMuteAsync(AudioMatch match, CancellationToken ct);

    Task<bool> TrySetVolumeAsync(AudioMatch match, float level, CancellationToken ct);

    Task<bool> TrySetMuteAsync(AudioMatch match, bool mute, CancellationToken ct);

    event EventHandler? Changed;
}
