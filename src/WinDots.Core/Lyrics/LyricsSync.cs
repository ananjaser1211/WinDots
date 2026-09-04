namespace WinDots.Core.Lyrics;

/// <summary>Pure timing helper: which synced lyric line is current for a playback position and user offset.</summary>
public static class LyricsSync
{
    /// <summary>
    /// Returns the index of the current line for <paramref name="position"/> given a user <paramref name="offset"/>
    /// (positive offset makes lines advance earlier). The current line is the last one whose timestamp is at or before
    /// the effective time. Returns -1 before the first line, or when there are no timestamped lines. Lines without a
    /// timestamp are ignored. The list is assumed sorted by <see cref="LyricsLine.At"/> (as <see cref="LrcParser"/> emits).
    /// </summary>
    public static int CurrentIndex(IReadOnlyList<LyricsLine> lines, TimeSpan position, TimeSpan offset)
    {
        ArgumentNullException.ThrowIfNull(lines);

        TimeSpan effective = position + offset;
        int current = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            TimeSpan? at = lines[i].At;
            if (at is null)
            {
                continue;
            }

            if (at.Value <= effective)
            {
                current = i;
            }
            else
            {
                break;
            }
        }

        return current;
    }
}
