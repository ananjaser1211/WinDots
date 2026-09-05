using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace WinDots.Core.Updates;

/// <summary>
/// A tolerant semantic-version value used to compare the running app version against a GitHub release tag.
/// Parsing accepts an optional leading <c>v</c>/<c>V</c>, a <c>major.minor.patch</c> core (missing minor/patch
/// default to 0, a redundant fourth numeric component such as a Windows package revision is ignored), an optional
/// <c>-preRelease</c> suffix, and optional <c>+buildMetadata</c> which is parsed but ignored. Comparison follows
/// SemVer 2.0.0 precedence: numeric core first, then a release outranks a pre-release, then dotted pre-release
/// identifiers (numeric identifiers numerically, others ordinally, fewer identifiers ranking lower). Pure and
/// deterministic; contains no Windows or I/O types. See _docs/10-enhancement-plan.md (E7).
/// </summary>
public sealed class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
{
    /// <param name="major">Major component (>= 0).</param>
    /// <param name="minor">Minor component (>= 0).</param>
    /// <param name="patch">Patch component (>= 0).</param>
    /// <param name="preRelease">Optional pre-release identifier string (without the leading '-'); null for a release.</param>
    public SemanticVersion(int major, int minor, int patch, string? preRelease = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(major);
        ArgumentOutOfRangeException.ThrowIfNegative(minor);
        ArgumentOutOfRangeException.ThrowIfNegative(patch);
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = string.IsNullOrEmpty(preRelease) ? null : preRelease;
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    /// <summary>The dotted pre-release string (e.g. <c>beta.1</c>), or null for a stable release.</summary>
    public string? PreRelease { get; }

    /// <summary>True when this is a pre-release (has a pre-release suffix).</summary>
    public bool IsPreRelease => PreRelease is not null;

    /// <summary>Parses a tag such as <c>v0.2.0</c> or <c>1.0.0-beta.1</c>; returns false for junk.</summary>
    public static bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out SemanticVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string s = text.Trim();
        if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V'))
        {
            s = s[1..];
        }

        // Strip build metadata (everything from the first '+'); it does not affect precedence.
        int plus = s.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            s = s[..plus];
        }

        // Split the pre-release suffix off at the first '-'.
        string core;
        string? pre = null;
        int dash = s.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0)
        {
            core = s[..dash];
            pre = s[(dash + 1)..];
            if (!IsValidPreRelease(pre))
            {
                return false;
            }
        }
        else
        {
            core = s;
        }

        if (core.Length == 0)
        {
            return false;
        }

        string[] parts = core.Split('.');

        // Accept up to four numeric core parts; the fourth (a package revision) is ignored.
        if (parts.Length is < 1 or > 4)
        {
            return false;
        }

        if (!TryParseComponent(parts[0], out int major))
        {
            return false;
        }

        int minor = 0;
        int patch = 0;
        if (parts.Length > 1 && !TryParseComponent(parts[1], out minor))
        {
            return false;
        }

        if (parts.Length > 2 && !TryParseComponent(parts[2], out patch))
        {
            return false;
        }

        if (parts.Length > 3 && !TryParseComponent(parts[3], out _))
        {
            return false;
        }

        version = new SemanticVersion(major, minor, patch, pre);
        return true;
    }

    /// <summary>Parses a tag; throws <see cref="FormatException"/> on junk.</summary>
    public static SemanticVersion Parse(string text)
    {
        if (!TryParse(text, out SemanticVersion? version))
        {
            throw new FormatException($"'{text}' is not a recognisable version.");
        }

        return version;
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        int c = Major.CompareTo(other.Major);
        if (c != 0)
        {
            return c;
        }

        c = Minor.CompareTo(other.Minor);
        if (c != 0)
        {
            return c;
        }

        c = Patch.CompareTo(other.Patch);
        if (c != 0)
        {
            return c;
        }

        // A release has higher precedence than a pre-release of the same core.
        if (PreRelease is null && other.PreRelease is null)
        {
            return 0;
        }

        if (PreRelease is null)
        {
            return 1;
        }

        if (other.PreRelease is null)
        {
            return -1;
        }

        return ComparePreRelease(PreRelease, other.PreRelease);
    }

    public bool Equals(SemanticVersion? other) =>
        other is not null &&
        Major == other.Major &&
        Minor == other.Minor &&
        Patch == other.Patch &&
        string.Equals(PreRelease, other.PreRelease, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as SemanticVersion);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, PreRelease);

    public override string ToString() =>
        PreRelease is null
            ? $"{Major}.{Minor}.{Patch}"
            : $"{Major}.{Minor}.{Patch}-{PreRelease}";

    public static bool operator ==(SemanticVersion? left, SemanticVersion? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(SemanticVersion? left, SemanticVersion? right) => !(left == right);

    public static bool operator <(SemanticVersion? left, SemanticVersion? right) => Compare(left, right) < 0;

    public static bool operator >(SemanticVersion? left, SemanticVersion? right) => Compare(left, right) > 0;

    public static bool operator <=(SemanticVersion? left, SemanticVersion? right) => Compare(left, right) <= 0;

    public static bool operator >=(SemanticVersion? left, SemanticVersion? right) => Compare(left, right) >= 0;

    private static int Compare(SemanticVersion? left, SemanticVersion? right)
    {
        if (left is null)
        {
            return right is null ? 0 : -1;
        }

        return left.CompareTo(right);
    }

    private static bool TryParseComponent(string part, out int value)
    {
        value = 0;

        // Reject signs, whitespace, and empty; require plain digits so "1.-1.0" or "1. .0" fail.
        if (part.Length == 0)
        {
            return false;
        }

        foreach (char ch in part)
        {
            if (ch is < '0' or > '9')
            {
                return false;
            }
        }

        return int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static bool IsValidPreRelease(string pre)
    {
        if (pre.Length == 0)
        {
            return false;
        }

        foreach (string id in pre.Split('.'))
        {
            if (id.Length == 0)
            {
                return false;
            }

            foreach (char ch in id)
            {
                bool ok = ch is (>= '0' and <= '9') or (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or '-';
                if (!ok)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static int ComparePreRelease(string left, string right)
    {
        string[] a = left.Split('.');
        string[] b = right.Split('.');
        int count = Math.Min(a.Length, b.Length);
        for (int i = 0; i < count; i++)
        {
            int c = CompareIdentifier(a[i], b[i]);
            if (c != 0)
            {
                return c;
            }
        }

        // All shared identifiers equal: the longer pre-release has higher precedence.
        return a.Length.CompareTo(b.Length);
    }

    private static int CompareIdentifier(string a, string b)
    {
        bool aNum = TryParseComponent(a, out int an);
        bool bNum = TryParseComponent(b, out int bn);

        if (aNum && bNum)
        {
            return an.CompareTo(bn);
        }

        // Numeric identifiers always have lower precedence than alphanumeric ones.
        if (aNum)
        {
            return -1;
        }

        if (bNum)
        {
            return 1;
        }

        return string.CompareOrdinal(a, b);
    }
}
