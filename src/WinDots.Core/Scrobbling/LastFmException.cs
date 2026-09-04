namespace WinDots.Core.Scrobbling;

/// <summary>
/// A Last.fm API error carrying the numeric error code from the response body. Callers use <see cref="Code"/> to decide
/// whether to re-authenticate (invalid session), keep polling (token not authorised), or retry later (transient).
/// See https://www.last.fm/api/errorcodes and _docs/10-enhancement-plan.md (E4).
/// </summary>
public sealed class LastFmException : Exception
{
    public LastFmException(int code, string message)
        : base(message) => Code = code;

    public LastFmException(int code, string message, Exception inner)
        : base(message, inner) => Code = code;

    /// <summary>The Last.fm numeric error code, or 0 when the failure was transport/parse rather than an API error.</summary>
    public int Code { get; }

    /// <summary>The token has not yet been authorised by the user in the browser; the sign-in poll should keep waiting.</summary>
    public bool IsTokenNotAuthorized => Code == 14;

    /// <summary>The stored session key is invalid or was revoked; the user must sign in again.</summary>
    public bool IsAuthFailure => Code is 4 or 9 or 10 or 13;

    /// <summary>A temporary server-side condition (offline, rate limited, unavailable); the queue should retry with backoff.</summary>
    public bool IsTransient => Code is 0 or 11 or 16 or 29;
}
