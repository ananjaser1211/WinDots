namespace WinDots.Core.Contracts;

/// <summary>
/// Resolves the real per-application icon for a media session's source application (its AUMID for packaged players,
/// or an executable name for unpackaged ones). Implemented in <c>WinDots.Windows</c> over the Shell / package APIs.
/// </summary>
/// <remarks>
/// The signature is BCL-only: the icon is returned as encoded image bytes (PNG or ICO) so no WinUI or COM type
/// crosses the layer boundary. Implementations must resolve off the UI thread, cache by app id, and never throw into
/// callers -- an unresolved icon returns <see langword="null"/>.
/// </remarks>
public interface IAppIconProvider
{
    /// <summary>
    /// Returns encoded icon bytes (PNG/ICO) for <paramref name="appId"/>, or <see langword="null"/> when no icon is
    /// available. Never throws; failures resolve to <see langword="null"/>.
    /// </summary>
    /// <param name="appId">The session's source application id (AUMID or executable name), as keyed by the coordinator.</param>
    Task<byte[]?> GetIconAsync(string appId, CancellationToken ct);
}
