using System.Globalization;
using System.Text.RegularExpressions;

namespace WinDots.Core.Lyrics;

/// <summary>The outcome of parsing an LRC (or plain) lyrics body: the lines and whether they carry timestamps.</summary>
public readonly record struct LrcParseResult(IReadOnlyList<LyricsLine> Lines, bool IsSynced);

/// <summary>
/// Pure LRC parser. Understands the <c>[mm:ss.xx]</c> timestamp form (also <c>[mm:ss]</c> and <c>[mm:ss.xxx]</c>),
/// multiple timestamps on one line (<c>[00:12.00][01:20.50]text</c>), and metadata tags (<c>[ar:..]</c>, <c>[ti:..]</c>)
/// which are ignored. When a body carries no timestamps it falls back to plain, unsynced lines. BCL only, deterministic.
/// See _docs/10-enhancement-plan.md (E3).
/// </summary>
public static class LrcParser
{
    // A timestamp tag: [mm:ss], [mm:ss.xx] or [mm:ss.xxx]. Minutes may exceed 59 for long tracks.
    private static readonly Regex TimestampTag = new(
        @"\[(?<min>\d{1,3}):(?<sec>[0-5]?\d)(?:[.:](?<frac>\d{1,3}))?\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Parses an LRC or plain lyrics body. Never returns null; an empty body yields no lines.</summary>
    public static LrcParseResult Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new LrcParseResult(Array.Empty<LyricsLine>(), IsSynced: false);
        }

        var synced = new List<LyricsLine>();
        string[] rawLines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        foreach (string raw in rawLines)
        {
            MatchCollection matches = TimestampTag.Matches(raw);
            if (matches.Count == 0)
            {
                continue;
            }

            // The lyric text is everything after the final timestamp tag on the line.
            Match last = matches[^1];
            string content = raw[(last.Index + last.Length)..].Trim();

            foreach (Match m in matches)
            {
                TimeSpan at = ToTimeSpan(m);
                synced.Add(new LyricsLine(at, content));
            }
        }

        if (synced.Count > 0)
        {
            synced.Sort(static (a, b) => Nullable.Compare(a.At, b.At));
            return new LrcParseResult(synced, IsSynced: true);
        }

        // No timestamps anywhere: plain, unsynced lines (blank lines dropped).
        var plain = new List<LyricsLine>();
        foreach (string raw in rawLines)
        {
            string trimmed = raw.Trim();
            if (trimmed.Length > 0)
            {
                plain.Add(new LyricsLine(At: null, trimmed));
            }
        }

        return new LrcParseResult(plain, IsSynced: false);
    }

    private static TimeSpan ToTimeSpan(Match m)
    {
        int minutes = int.Parse(m.Groups["min"].Value, CultureInfo.InvariantCulture);
        int seconds = int.Parse(m.Groups["sec"].Value, CultureInfo.InvariantCulture);
        int millis = 0;
        if (m.Groups["frac"].Success)
        {
            string frac = m.Groups["frac"].Value;
            // Normalise the fraction to milliseconds: "5" -> 500 ms, "50" -> 500 ms, "500" -> 500 ms.
            frac = frac.Length switch
            {
                1 => frac + "00",
                2 => frac + "0",
                _ => frac[..3],
            };
            millis = int.Parse(frac, CultureInfo.InvariantCulture);
        }

        return new TimeSpan(0, 0, minutes, seconds, millis);
    }
}
