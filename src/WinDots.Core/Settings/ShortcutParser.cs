using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace WinDots.Core.Settings;

/// <summary>Keyboard modifier flags for a <see cref="Shortcut"/>.</summary>
[Flags]
public enum ShortcutModifiers
{
    None = 0,
    Win = 1 << 0,
    Ctrl = 1 << 1,
    Alt = 1 << 2,
    Shift = 1 << 3,
}

/// <summary>A parsed keyboard chord: zero or more modifiers plus one non-modifier virtual key.</summary>
public sealed record Shortcut(ShortcutModifiers Modifiers, int Key)
{
    public override string ToString() => ShortcutParser.Format(this);
}

/// <summary>Parses and formats "Win+Shift+M"-style chords. See _docs/06-settings-schema.md.</summary>
public static class ShortcutParser
{
    private static readonly Dictionary<string, int> KeyNames = BuildKeyNames();
    private static readonly Dictionary<int, string> KeyCodes = BuildKeyCodes();

    /// <summary>Parses a chord string; throws <see cref="FormatException"/> when invalid.</summary>
    public static Shortcut Parse(string text)
    {
        if (!TryParse(text, out Shortcut? shortcut))
        {
            throw new FormatException($"'{text}' is not a valid shortcut.");
        }

        return shortcut;
    }

    /// <summary>
    /// Attempts to parse a chord. Fails when the text is empty, contains an unknown token, is missing a
    /// non-modifier key, or names more than one non-modifier key.
    /// </summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out Shortcut? shortcut)
    {
        shortcut = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        ShortcutModifiers modifiers = ShortcutModifiers.None;
        int? key = null;

        foreach (string raw in text.Split('+'))
        {
            string token = raw.Trim();
            if (token.Length == 0)
            {
                return false;
            }

            ShortcutModifiers? modifier = MatchModifier(token);
            if (modifier is not null)
            {
                modifiers |= modifier.Value;
                continue;
            }

            if (!KeyNames.TryGetValue(token.ToUpperInvariant(), out int vk))
            {
                return false;
            }

            if (key is not null)
            {
                return false;
            }

            key = vk;
        }

        if (key is null)
        {
            return false;
        }

        shortcut = new Shortcut(modifiers, key.Value);
        return true;
    }

    /// <summary>Formats a shortcut back to canonical "Win+Ctrl+Alt+Shift+Key" order.</summary>
    public static string Format(Shortcut shortcut)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        StringBuilder sb = new();
        if (shortcut.Modifiers.HasFlag(ShortcutModifiers.Win))
        {
            sb.Append("Win+");
        }

        if (shortcut.Modifiers.HasFlag(ShortcutModifiers.Ctrl))
        {
            sb.Append("Ctrl+");
        }

        if (shortcut.Modifiers.HasFlag(ShortcutModifiers.Alt))
        {
            sb.Append("Alt+");
        }

        if (shortcut.Modifiers.HasFlag(ShortcutModifiers.Shift))
        {
            sb.Append("Shift+");
        }

        sb.Append(KeyCodes.TryGetValue(shortcut.Key, out string? name)
            ? name
            : $"0x{shortcut.Key.ToString("X2", CultureInfo.InvariantCulture)}");
        return sb.ToString();
    }

    private static ShortcutModifiers? MatchModifier(string token) => token.ToUpperInvariant() switch
    {
        "WIN" or "WINDOWS" or "META" => ShortcutModifiers.Win,
        "CTRL" or "CONTROL" => ShortcutModifiers.Ctrl,
        "ALT" => ShortcutModifiers.Alt,
        "SHIFT" => ShortcutModifiers.Shift,
        _ => null,
    };

    private static Dictionary<string, int> BuildKeyNames()
    {
        Dictionary<string, int> map = new(StringComparer.Ordinal);

        for (char c = 'A'; c <= 'Z'; c++)
        {
            map[c.ToString()] = c;
        }

        for (char c = '0'; c <= '9'; c++)
        {
            map[c.ToString()] = c;
        }

        for (int i = 1; i <= 24; i++)
        {
            map["F" + i.ToString(CultureInfo.InvariantCulture)] = 0x70 + (i - 1);
        }

        map["SPACE"] = 0x20;
        map["ESCAPE"] = 0x1B;
        map["ESC"] = 0x1B;
        map["ENTER"] = 0x0D;
        map["RETURN"] = 0x0D;
        map["LEFT"] = 0x25;
        map["UP"] = 0x26;
        map["RIGHT"] = 0x27;
        map["DOWN"] = 0x28;
        map["HOME"] = 0x24;
        map["END"] = 0x23;
        map["PAGEUP"] = 0x21;
        map["PAGEDOWN"] = 0x22;
        map["INSERT"] = 0x2D;
        map["DELETE"] = 0x2E;
        return map;
    }

    private static Dictionary<int, string> BuildKeyCodes()
    {
        Dictionary<int, string> map = new();

        for (char c = 'A'; c <= 'Z'; c++)
        {
            map[c] = c.ToString();
        }

        for (char c = '0'; c <= '9'; c++)
        {
            map[c] = c.ToString();
        }

        for (int i = 1; i <= 24; i++)
        {
            map[0x70 + (i - 1)] = "F" + i.ToString(CultureInfo.InvariantCulture);
        }

        map[0x20] = "Space";
        map[0x1B] = "Escape";
        map[0x0D] = "Enter";
        map[0x25] = "Left";
        map[0x26] = "Up";
        map[0x27] = "Right";
        map[0x28] = "Down";
        map[0x24] = "Home";
        map[0x23] = "End";
        map[0x21] = "PageUp";
        map[0x22] = "PageDown";
        map[0x2D] = "Insert";
        map[0x2E] = "Delete";
        return map;
    }
}
