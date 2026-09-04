using System.Security.Cryptography;
using System.Text;

namespace WinDots.Core.Scrobbling;

/// <summary>
/// Computes the Last.fm <c>api_sig</c> for a request. Per the Last.fm authentication spec the caller's parameters
/// (excluding <c>format</c> and <c>callback</c>) are sorted by name (ordinal), concatenated as <c>name+value</c> with no
/// separators, the shared secret is appended, and the UTF-8 bytes are MD5-hashed and rendered as lower-case hex. Pure and
/// BCL-only. See _docs/10-enhancement-plan.md (E4).
/// </summary>
public static class LastFmSigner
{
    /// <summary>
    /// Returns the lower-case hex MD5 signature for <paramref name="parameters"/> using <paramref name="secret"/>.
    /// The <c>format</c> and <c>callback</c> keys, and any null values, are excluded from the signature base string.
    /// </summary>
    public static string Sign(IReadOnlyDictionary<string, string> parameters, string secret)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(secret);

        var builder = new StringBuilder();
        foreach (KeyValuePair<string, string> pair in parameters
                     .Where(p => p.Value is not null &&
                                 !string.Equals(p.Key, "format", StringComparison.Ordinal) &&
                                 !string.Equals(p.Key, "callback", StringComparison.Ordinal))
                     .OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            builder.Append(pair.Key).Append(pair.Value);
        }

        builder.Append(secret);
        byte[] digest = MD5.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexStringLower(digest);
    }
}
