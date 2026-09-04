using WinDots.Core.Contracts;

namespace WinDots.Core.Audio;

/// <summary>Whether a source application id resolved to a single package or to a plain executable name.</summary>
public enum AudioSourceKind
{
    /// <summary>An AUMID resolved to one package family; every candidate process belongs to that package.</summary>
    Package,

    /// <summary>A plain executable name; candidate processes may be unrelated instances of that image.</summary>
    Executable,
}

/// <summary>One Core Audio session as seen on the render endpoint, reduced to the facts the policy needs.</summary>
public readonly record struct AudioSessionInfo(uint ProcessId, string SessionIdentifier, bool IsSharedHost);

/// <summary>Outcome of matching: the confidence tier, the identifiers volume/mute apply to, and why.</summary>
public sealed record AudioMatchResult(AudioMatchConfidence Confidence, IReadOnlyList<string> SessionIdentifiers, string Explanation);

/// <summary>
/// Pure Core Audio matching policy: given the running process ids that belong to a media source and the audio
/// sessions on the default render endpoint, decide the confidence tier and which sessions volume applies to. No COM,
/// no titles, deterministic. See <c>_docs/05-architecture.md</c> "Core Audio matching". Exercised by
/// <c>CoreAudioSessionProvider</c>; unit-tested without any platform dependency.
/// </summary>
public static class AudioMatchPolicy
{
    /// <summary>Process image names (without extension) that host audio for many unrelated apps and never identify one source.</summary>
    public static readonly IReadOnlyList<string> SharedHostProcessNames = new[]
    {
        "audiodg",
        "RuntimeBroker",
        "svchost",
    };

    /// <summary>True when <paramref name="processName"/> (with or without .exe) is a shared audio host, never a specific source.</summary>
    public static bool IsSharedHost(string? processName)
    {
        if (string.IsNullOrEmpty(processName))
        {
            return false;
        }

        var name = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName[..^4]
            : processName;

        foreach (var host in SharedHostProcessNames)
        {
            if (string.Equals(name, host, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Score the render-endpoint <paramref name="sessions"/> against the source's running <paramref name="candidatePids"/>.
    /// </summary>
    public static AudioMatchResult Evaluate(
        AudioSourceKind sourceKind,
        IReadOnlyCollection<uint> candidatePids,
        IReadOnlyList<AudioSessionInfo> sessions)
    {
        ArgumentNullException.ThrowIfNull(candidatePids);
        ArgumentNullException.ThrowIfNull(sessions);

        if (candidatePids.Count == 0)
        {
            return new AudioMatchResult(AudioMatchConfidence.None, Array.Empty<string>(), "None: no running process for the source application.");
        }

        var pidSet = candidatePids as HashSet<uint> ?? new HashSet<uint>(candidatePids);

        var matched = new List<AudioSessionInfo>();
        var sharedOnly = new List<AudioSessionInfo>();
        foreach (var session in sessions)
        {
            if (!pidSet.Contains(session.ProcessId))
            {
                continue;
            }

            if (session.IsSharedHost)
            {
                sharedOnly.Add(session);
            }
            else
            {
                matched.Add(session);
            }
        }

        if (matched.Count == 0)
        {
            return sharedOnly.Count > 0
                ? new AudioMatchResult(AudioMatchConfidence.None, Array.Empty<string>(), $"None: {sharedOnly.Count} matching session(s) belong only to shared host processes.")
                : new AudioMatchResult(AudioMatchConfidence.None, Array.Empty<string>(), "None: no audio session on the render endpoint matched a candidate process.");
        }

        var ids = matched.Select(m => m.SessionIdentifier).ToArray();
        var distinctPids = matched.Select(m => m.ProcessId).Distinct().Count();

        if (matched.Count == 1)
        {
            return new AudioMatchResult(AudioMatchConfidence.High, ids, "High: exactly one audio session matched the source process.");
        }

        if (sourceKind == AudioSourceKind.Package)
        {
            return new AudioMatchResult(AudioMatchConfidence.High, ids, $"High: {matched.Count} audio session(s) across {distinctPids} process(es), all in the same package.");
        }

        return new AudioMatchResult(AudioMatchConfidence.Medium, ids, $"Medium: {matched.Count} audio session(s) across {distinctPids} process(es) of the same executable.");
    }
}
