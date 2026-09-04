using WinDots.Core.Settings;

namespace WinDots.Core.Tests.Settings;

public class ShortcutSuggesterTests
{
    private static Shortcut Chord(string text) => ShortcutParser.Parse(text);

    [Fact]
    public void PreservesTheKey_AcrossEverySuggestion()
    {
        Shortcut desired = Chord("Win+Shift+M");

        IReadOnlyList<Shortcut> suggestions = ShortcutSuggester.Suggest(desired);

        Assert.NotEmpty(suggestions);
        Assert.All(suggestions, s => Assert.Equal(desired.Key, s.Key));
    }

    [Fact]
    public void NeverSuggestsTheDesiredChord()
    {
        Shortcut desired = Chord("Win+Shift+M");

        IReadOnlyList<Shortcut> suggestions = ShortcutSuggester.Suggest(desired);

        Assert.DoesNotContain(desired, suggestions);
    }

    [Fact]
    public void OrderIsStableAndDeterministic()
    {
        Shortcut desired = Chord("Win+Shift+M");

        IReadOnlyList<Shortcut> first = ShortcutSuggester.Suggest(desired);
        IReadOnlyList<Shortcut> second = ShortcutSuggester.Suggest(desired);

        Assert.Equal(first, second);

        // The first few, in the documented preference order (Win+Shift is the desired set and is skipped).
        Assert.Equal(Chord("Ctrl+Alt+M"), first[0]);
        Assert.Equal(Chord("Ctrl+Shift+M"), first[1]);
        Assert.Equal(Chord("Win+Alt+M"), first[2]);
        Assert.Equal(Chord("Alt+Shift+M"), first[3]);
    }

    [Fact]
    public void ContainsNoDuplicates()
    {
        Shortcut desired = Chord("Win+Shift+M");

        IReadOnlyList<Shortcut> suggestions = ShortcutSuggester.Suggest(desired);

        Assert.Equal(suggestions.Count, suggestions.Distinct().Count());
    }

    [Fact]
    public void OnlyTwoOrMoreModifiers_NeverBareSingleModifier()
    {
        Shortcut desired = Chord("Win+Shift+M");

        IReadOnlyList<Shortcut> suggestions = ShortcutSuggester.Suggest(desired);

        Assert.All(suggestions, s => Assert.True(CountModifiers(s.Modifiers) >= 2));
    }

    [Fact]
    public void RespectsTheAvoidSet()
    {
        Shortcut desired = Chord("Win+Shift+M");
        var avoid = new[] { Chord("Ctrl+Alt+M"), Chord("Ctrl+Shift+M") };

        IReadOnlyList<Shortcut> suggestions = ShortcutSuggester.Suggest(desired, avoid);

        Assert.DoesNotContain(Chord("Ctrl+Alt+M"), suggestions);
        Assert.DoesNotContain(Chord("Ctrl+Shift+M"), suggestions);
        // The next-best surfaces to the front once the taken ones are removed.
        Assert.Equal(Chord("Win+Alt+M"), suggestions[0]);
    }

    [Fact]
    public void SkipsWellKnownReservedCombos()
    {
        // Win+Shift+S is reserved (Snip & Sketch), so with key S it must not be suggested for any desired chord.
        Shortcut desired = Chord("Ctrl+Alt+S");

        IReadOnlyList<Shortcut> suggestions = ShortcutSuggester.Suggest(desired);

        Assert.DoesNotContain(Chord("Win+Shift+S"), suggestions);
    }

    [Fact]
    public void SuggestFirst_ReturnsTheLeadingSuggestion()
    {
        Shortcut desired = Chord("Win+Shift+M");

        Shortcut? first = ShortcutSuggester.SuggestFirst(desired);

        Assert.Equal(Chord("Ctrl+Alt+M"), first);
    }

    [Fact]
    public void SuggestFirst_SkipsTakenChords()
    {
        Shortcut desired = Chord("Win+Shift+M");
        var avoid = new[] { Chord("Ctrl+Alt+M") };

        Shortcut? first = ShortcutSuggester.SuggestFirst(desired, avoid);

        Assert.Equal(Chord("Ctrl+Shift+M"), first);
    }

    private static int CountModifiers(ShortcutModifiers modifiers)
    {
        int count = 0;
        foreach (ShortcutModifiers flag in new[]
                 {
                     ShortcutModifiers.Win, ShortcutModifiers.Ctrl, ShortcutModifiers.Alt, ShortcutModifiers.Shift,
                 })
        {
            if (modifiers.HasFlag(flag))
            {
                count++;
            }
        }

        return count;
    }
}
