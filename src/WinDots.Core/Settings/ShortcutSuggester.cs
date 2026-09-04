namespace WinDots.Core.Settings;

/// <summary>
/// Suggests alternative chords for a desired shortcut whose combination is unavailable (typically because another app
/// already owns it). The non-modifier key is preserved; only the modifier set varies. The order is fixed and
/// deterministic, favouring the two-modifier combinations that are least likely to clash, then the three- and
/// four-modifier ones. Candidates in the caller's avoid-set, the desired chord itself, bare single-modifier combos,
/// and a small set of well-known OS combinations are skipped. Pure and BCL-only so it is unit-testable.
/// </summary>
public static class ShortcutSuggester
{
    // Fixed preference order of modifier sets. Single-modifier and empty sets are intentionally absent: a bare
    // modifier+key is too easily reserved and too weak a global chord to suggest.
    private static readonly ShortcutModifiers[] ModifierOrder =
    {
        ShortcutModifiers.Ctrl | ShortcutModifiers.Alt,
        ShortcutModifiers.Ctrl | ShortcutModifiers.Shift,
        ShortcutModifiers.Win | ShortcutModifiers.Alt,
        ShortcutModifiers.Alt | ShortcutModifiers.Shift,
        ShortcutModifiers.Win | ShortcutModifiers.Ctrl,
        ShortcutModifiers.Win | ShortcutModifiers.Shift,
        ShortcutModifiers.Ctrl | ShortcutModifiers.Alt | ShortcutModifiers.Shift,
        ShortcutModifiers.Win | ShortcutModifiers.Ctrl | ShortcutModifiers.Alt,
        ShortcutModifiers.Win | ShortcutModifiers.Ctrl | ShortcutModifiers.Shift,
        ShortcutModifiers.Win | ShortcutModifiers.Alt | ShortcutModifiers.Shift,
        ShortcutModifiers.Win | ShortcutModifiers.Ctrl | ShortcutModifiers.Alt | ShortcutModifiers.Shift,
    };

    // A small, well-known set of OS-reserved multi-modifier chords that must never be suggested. Single-Win combos
    // (Win+L, Win+D, …) are already excluded by ModifierOrder having no single-modifier entries.
    private static readonly IReadOnlySet<Shortcut> Reserved = BuildReserved();

    /// <summary>
    /// Returns alternative chords for <paramref name="desired"/>, most-recommended first, keeping the same key and
    /// varying the modifiers. Skips the desired chord, anything in <paramref name="avoid"/>, and well-known OS combos.
    /// </summary>
    public static IReadOnlyList<Shortcut> Suggest(Shortcut desired, IEnumerable<Shortcut>? avoid = null)
    {
        ArgumentNullException.ThrowIfNull(desired);

        var taken = new HashSet<Shortcut>();
        if (avoid is not null)
        {
            foreach (Shortcut chord in avoid)
            {
                _ = taken.Add(chord);
            }
        }

        int key = desired.Key;
        var results = new List<Shortcut>();
        var seen = new HashSet<Shortcut>();

        foreach (ShortcutModifiers mods in ModifierOrder)
        {
            if (mods == desired.Modifiers)
            {
                continue;
            }

            var candidate = new Shortcut(mods, key);
            if (taken.Contains(candidate) || Reserved.Contains(candidate) || !seen.Add(candidate))
            {
                continue;
            }

            results.Add(candidate);
        }

        return results;
    }

    /// <summary>
    /// Convenience: the single best suggestion for <paramref name="desired"/> given the taken chords, or
    /// <see langword="null"/> when every candidate is excluded.
    /// </summary>
    public static Shortcut? SuggestFirst(Shortcut desired, IEnumerable<Shortcut>? avoid = null)
    {
        IReadOnlyList<Shortcut> all = Suggest(desired, avoid);
        return all.Count > 0 ? all[0] : null;
    }

    private static IReadOnlySet<Shortcut> BuildReserved()
    {
        string[] chords =
        {
            "Win+Shift+S",          // Snip & Sketch
            "Ctrl+Shift+Escape",    // Task Manager
            "Win+Ctrl+D",           // new virtual desktop
            "Win+Ctrl+Left",        // previous virtual desktop
            "Win+Ctrl+Right",       // next virtual desktop
            "Win+Ctrl+F4",          // close virtual desktop
            "Win+Shift+Left",       // move window to left monitor
            "Win+Shift+Right",      // move window to right monitor
        };

        var set = new HashSet<Shortcut>();
        foreach (string text in chords)
        {
            if (ShortcutParser.TryParse(text, out Shortcut? chord))
            {
                _ = set.Add(chord);
            }
        }

        return set;
    }
}
