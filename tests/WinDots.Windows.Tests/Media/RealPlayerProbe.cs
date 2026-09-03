using System.Text.Json;
using System.Text.Json.Serialization;
using WinDots.Core.Contracts;
using WinDots.Core.Media;
using WinDots.Windows.Media;
using Xunit.Abstractions;

namespace WinDots.Windows.Tests.Media;

/// <summary>
/// Manual compatibility probe for the matrix in _docs/07-testing-and-compatibility.md.
/// Set WINDOTS_PROBE_APP to a substring of the player's AUMID (for example "vlc", "msedge", "ZuneMusic"),
/// start the player with something playing, then run:
///   dotnet test tests/WinDots.Windows.Tests -p:Platform=x64 --filter "FullyQualifiedName~RealPlayerProbe" --logger "console;verbosity=detailed"
/// Without the variable the probe reports "skipped" and passes.
/// </summary>
[Trait("Category", "Platform")]
public class RealPlayerProbe(ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public async Task ProbeConfiguredPlayer()
    {
        var target = Environment.GetEnvironmentVariable("WINDOTS_PROBE_APP");
        if (string.IsNullOrWhiteSpace(target))
        {
            output.WriteLine("skipped: WINDOTS_PROBE_APP not set");
            return;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = cts.Token;
        await using var provider = new GsmtcSessionProvider();
        await provider.InitializeAsync(ct);

        output.WriteLine($"Sessions: {string.Join(", ", provider.Sessions.Select(s => s.SourceAppId))}");
        var session = provider.Sessions.FirstOrDefault(s => s.SourceAppId.Contains(target, StringComparison.OrdinalIgnoreCase));
        Assert.True(session is not null, $"No session whose AUMID contains '{target}'.");

        await Task.Delay(500, ct);
        var before = session.Current;
        output.WriteLine("Snapshot:");
        output.WriteLine(JsonSerializer.Serialize(before, Json));

        var art = await session.LoadArtworkAsync(8 * 1024 * 1024, ct);
        output.WriteLine($"Artwork: success={art.Success} bytes={art.Bytes.Length} type={art.ContentType} error={art.Error}");

        await Report("PlayPause (1)", session.TryPlayPauseAsync(ct));
        var afterToggle = await WaitForChangeAsync(session, s => s.State != before.State, ct);
        output.WriteLine($"State after toggle: {afterToggle?.State.ToString() ?? "no change observed"}");
        await Report("PlayPause (2)", session.TryPlayPauseAsync(ct));
        await WaitForChangeAsync(session, s => s.State == before.State, ct);

        if (before.Can(Capabilities.Seek) && before.Timeline.HasDuration)
        {
            var target2 = TimelineInterpolator.Displayed(before.Timeline, before.State, DateTimeOffset.UtcNow) + TimeSpan.FromSeconds(5);
            await Report($"Seek to {TimeFormat.Clock(target2)}", session.TrySeekAsync(target2, ct));
            // Playback keeps advancing while we wait, so accept anything from just before the target to ten seconds past it.
            var seeked = await WaitForChangeAsync(session, s => s.Timeline.Position >= target2 - TimeSpan.FromSeconds(2) && s.Timeline.Position <= target2 + TimeSpan.FromSeconds(10), ct);
            output.WriteLine($"Position after seek: {(seeked is null ? "not confirmed" : TimeFormat.Clock(seeked.Timeline.Position))}");
        }
        else
        {
            output.WriteLine("Seek: not advertised");
        }

        if (before.Can(Capabilities.Next))
        {
            await Report("Next", session.TryNextAsync(ct));
            await Task.Delay(1500, ct);
            output.WriteLine($"Title after Next: {session.Current.Title}");
        }

        if (before.Can(Capabilities.Previous))
        {
            await Report("Previous", session.TryPreviousAsync(ct));
            await Task.Delay(1500, ct);
            output.WriteLine($"Title after Previous: {session.Current.Title}");
        }

        output.WriteLine("Final snapshot:");
        output.WriteLine(JsonSerializer.Serialize(session.Current, Json));
    }

    private async Task Report(string name, Task<CommandResult> command)
    {
        var r = await command;
        output.WriteLine($"{name}: {r.Status} {r.Message}");
    }

    private static async Task<MediaSnapshot?> WaitForChangeAsync(IMediaSession session, Func<MediaSnapshot, bool> predicate, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(6);
        while (DateTime.UtcNow < deadline)
        {
            var s = session.Current;
            if (predicate(s))
            {
                return s;
            }

            await Task.Delay(100, ct);
        }

        return null;
    }
}
