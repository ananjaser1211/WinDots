using WinDots.Core.Settings;

namespace WinDots.Core.Tests.Settings;

public class ShortcutParserTests
{
    [Theory]
    [InlineData("Win+Shift+M", ShortcutModifiers.Win | ShortcutModifiers.Shift, 'M')]
    [InlineData("Ctrl+Alt+Delete", ShortcutModifiers.Ctrl | ShortcutModifiers.Alt, 0x2E)]
    [InlineData("M", ShortcutModifiers.None, 'M')]
    [InlineData("ctrl+space", ShortcutModifiers.Ctrl, 0x20)]
    [InlineData("Win+F12", ShortcutModifiers.Win, 0x7B)]
    [InlineData("Alt+Left", ShortcutModifiers.Alt, 0x25)]
    [InlineData("Shift+5", ShortcutModifiers.Shift, '5')]
    [InlineData("Ctrl + Enter", ShortcutModifiers.Ctrl, 0x0D)]
    public void ParsesValidChords(string text, ShortcutModifiers modifiers, int key)
    {
        Assert.True(ShortcutParser.TryParse(text, out var shortcut));
        Assert.Equal(modifiers, shortcut!.Modifiers);
        Assert.Equal(key, shortcut.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Win+Ctrl")]        // no non-modifier key
    [InlineData("Shift")]           // modifier only
    [InlineData("Ctrl+Bogus")]      // unknown key name
    [InlineData("Win+A+B")]         // two non-modifier keys
    [InlineData("Win++M")]          // empty token
    [InlineData("F25")]             // out of F1-F24 range
    public void RejectsInvalidChords(string text)
    {
        Assert.False(ShortcutParser.TryParse(text, out _));
    }

    [Fact]
    public void ParseThrowsOnInvalid()
    {
        Assert.Throws<FormatException>(() => ShortcutParser.Parse("Ctrl"));
    }

    [Theory]
    [InlineData("Win+Shift+M", "Win+Shift+M")]
    [InlineData("shift+win+m", "Win+Shift+M")]
    [InlineData("ctrl+alt+delete", "Ctrl+Alt+Delete")]
    [InlineData("f1", "F1")]
    [InlineData("space", "Space")]
    public void FormatRoundTripsToCanonical(string input, string expected)
    {
        var shortcut = ShortcutParser.Parse(input);
        Assert.Equal(expected, ShortcutParser.Format(shortcut));
        Assert.Equal(expected, shortcut.ToString());
    }
}
