using WinDots.Core.Audio;
using WinDots.Core.Contracts;

namespace WinDots.Core.Tests.Audio;

public class AudioMatchPolicyTests
{
    private static AudioSessionInfo Session(uint pid, string id, bool shared = false) => new(pid, id, shared);

    [Fact]
    public void NoCandidates_IsNone()
    {
        var result = AudioMatchPolicy.Evaluate(AudioSourceKind.Executable, Array.Empty<uint>(), new[] { Session(10, "a") });

        Assert.Equal(AudioMatchConfidence.None, result.Confidence);
        Assert.Empty(result.SessionIdentifiers);
        Assert.Contains("no running process", result.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CandidatesButNoMatchingSession_IsNone()
    {
        var result = AudioMatchPolicy.Evaluate(AudioSourceKind.Executable, new uint[] { 42 }, new[] { Session(10, "a") });

        Assert.Equal(AudioMatchConfidence.None, result.Confidence);
        Assert.Empty(result.SessionIdentifiers);
    }

    [Fact]
    public void OnlySharedHostSessionsMatch_IsNone()
    {
        var result = AudioMatchPolicy.Evaluate(
            AudioSourceKind.Executable,
            new uint[] { 10 },
            new[] { Session(10, "audiodg-session", shared: true) });

        Assert.Equal(AudioMatchConfidence.None, result.Confidence);
        Assert.Empty(result.SessionIdentifiers);
        Assert.Contains("shared host", result.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExactlyOneMatchingSession_IsHigh()
    {
        var result = AudioMatchPolicy.Evaluate(
            AudioSourceKind.Executable,
            new uint[] { 10, 11 },
            new[] { Session(10, "sess-one"), Session(99, "other") });

        Assert.Equal(AudioMatchConfidence.High, result.Confidence);
        Assert.Equal(new[] { "sess-one" }, result.SessionIdentifiers);
    }

    [Fact]
    public void MultipleSessionsSamePackage_IsHigh()
    {
        var result = AudioMatchPolicy.Evaluate(
            AudioSourceKind.Package,
            new uint[] { 10, 11 },
            new[] { Session(10, "s1"), Session(11, "s2") });

        Assert.Equal(AudioMatchConfidence.High, result.Confidence);
        Assert.Equal(new[] { "s1", "s2" }, result.SessionIdentifiers);
        Assert.Contains("same package", result.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MultipleSessionsAcrossExeProcesses_IsMedium()
    {
        var result = AudioMatchPolicy.Evaluate(
            AudioSourceKind.Executable,
            new uint[] { 10, 11 },
            new[] { Session(10, "tab1"), Session(11, "tab2") });

        Assert.Equal(AudioMatchConfidence.Medium, result.Confidence);
        Assert.Equal(new[] { "tab1", "tab2" }, result.SessionIdentifiers);
        Assert.Contains("Medium", result.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedHostSessionsExcludedButRealMatchWins()
    {
        // A candidate PID that also has a shared-host session must still match on its real session.
        var result = AudioMatchPolicy.Evaluate(
            AudioSourceKind.Executable,
            new uint[] { 10 },
            new[] { Session(10, "real"), Session(10, "broker", shared: true) });

        Assert.Equal(AudioMatchConfidence.High, result.Confidence);
        Assert.Equal(new[] { "real" }, result.SessionIdentifiers);
    }

    [Theory]
    [InlineData("audiodg")]
    [InlineData("audiodg.exe")]
    [InlineData("RUNTIMEBROKER")]
    [InlineData("svchost.exe")]
    public void IsSharedHost_RecognisesKnownHosts(string name) => Assert.True(AudioMatchPolicy.IsSharedHost(name));

    [Theory]
    [InlineData("spotify")]
    [InlineData("chrome.exe")]
    [InlineData("")]
    [InlineData(null)]
    public void IsSharedHost_RejectsOthers(string? name) => Assert.False(AudioMatchPolicy.IsSharedHost(name));
}
