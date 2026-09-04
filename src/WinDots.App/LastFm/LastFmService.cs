using System.Net.Http;
using Microsoft.UI.Dispatching;
using WinDots.App.Diagnostics;
using WinDots.Core.Contracts;
using WinDots.Core.Media;
using WinDots.Core.Scrobbling;
using WinDots.Core.Settings;

namespace WinDots.App.LastFm;

/// <summary>
/// Runtime Last.fm integration. Watches the active session, sends now-playing on track start, scrobbles qualified plays
/// (via <see cref="ScrobbleQualifier"/> and a disk-backed <see cref="ScrobbleQueue"/> with backoff), and exposes the
/// love/unlove and sign-in operations the UI drives. Nothing is sent while disabled or signed out; titles and secrets are
/// never logged. Credentials live in the injected <see cref="ISecretStore"/>. Everything runs on the UI dispatcher; the
/// network calls are async. See _docs/10-enhancement-plan.md (E4) and _docs/privacy.md.
/// </summary>
public sealed class LastFmService : IDisposable
{
    private const string SessionKeyName = "lastfm.sessionKey";
    private const string UserNameKey = "lastfm.username";
    private const string ApiKeyName = "lastfm.apiKey";
    private const string ApiSecretName = "lastfm.apiSecret";

    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PollTimeout = TimeSpan.FromMinutes(5);

    private readonly ISecretStore _secrets;
    private readonly HttpMessageHandler _handler;
    private readonly SessionCoordinator _coordinator;
    private readonly DispatcherQueue _dispatcher;
    private readonly ScrobbleQualifier _qualifier = new();
    private readonly ScrobbleQueue _queue;
    private readonly DispatcherQueueTimer _timer;
    private readonly HashSet<string> _loved = new(StringComparer.Ordinal);

    private LastFmClient? _client;
    private string _apiKey = string.Empty;
    private string _apiSecret = string.Empty;
    private string? _sessionKey;
    private LastFmSettings _settings = new();
    private string? _lastNowPlayingKey;
    private bool _draining;
    private bool _disposed;

    public LastFmService(
        ISecretStore secrets,
        HttpMessageHandler handler,
        SessionCoordinator coordinator,
        DispatcherQueue dispatcher,
        string queuePath)
    {
        _secrets = secrets;
        _handler = handler;
        _coordinator = coordinator;
        _dispatcher = dispatcher;
        _queue = new ScrobbleQueue(queuePath);

        _timer = _dispatcher.CreateTimer();
        _timer.Interval = TickInterval;
        _timer.IsRepeating = true;
        _timer.Tick += OnTick;
    }

    /// <summary>Raised when the signed-in state, username, or settings change (the UI refreshes from this).</summary>
    public event EventHandler? StateChanged;

    /// <summary>True when a usable API key/secret exists (build-time or user-provided).</summary>
    public bool HasApiKey => _apiKey.Length > 0 && _apiSecret.Length > 0;

    /// <summary>True when a session key is stored (the user has signed in).</summary>
    public bool IsSignedIn => !string.IsNullOrEmpty(_sessionKey);

    /// <summary>The signed-in username, or null.</summary>
    public string? Username { get; private set; }

    /// <summary>The signed-in user's avatar URL, or null.</summary>
    public string? AvatarUrl { get; private set; }

    /// <summary>Whether the integration is enabled in settings.</summary>
    public bool Enabled => _settings.Enabled;

    /// <summary>Loads the effective key/secret and any stored session, then starts the runtime timer.</summary>
    public async Task InitializeAsync(LastFmSettings settings, CancellationToken ct)
    {
        _settings = settings;
        await LoadKeysAsync(ct).ConfigureAwait(true);
        _sessionKey = await _secrets.GetAsync(SessionKeyName, ct).ConfigureAwait(true);
        Username = await _secrets.GetAsync(UserNameKey, ct).ConfigureAwait(true);
        RebuildClient();
        _timer.Start();

        if (IsSignedIn && HasApiKey)
        {
            _ = RefreshUserInfoAsync(CancellationToken.None);
        }

        RaiseStateChanged();
    }

    /// <summary>Applies updated settings (enabled/scrobble/nowPlaying).</summary>
    public void ApplySettings(LastFmSettings settings)
    {
        _settings = settings;
        if (!settings.Enabled)
        {
            _qualifier.Reset();
            _lastNowPlayingKey = null;
        }

        RaiseStateChanged();
    }

    /// <summary>
    /// Validates a user-provided key/secret by requesting a token, then stores them in the secret store. Returns true on
    /// success. Used by the "Create a key" helper when the build has no embedded key.
    /// </summary>
    public async Task<bool> ValidateAndStoreKeyAsync(string apiKey, string apiSecret, CancellationToken ct)
    {
        apiKey = apiKey.Trim();
        apiSecret = apiSecret.Trim();
        if (apiKey.Length == 0 || apiSecret.Length == 0)
        {
            return false;
        }

        using var probe = new LastFmClient(_handler, apiKey, apiSecret, ShellLog.Write);
        try
        {
            await probe.GetTokenAsync(ct).ConfigureAwait(true);
        }
        catch (LastFmException ex)
        {
            ShellLog.Write($"last.fm: key validation failed (code {ex.Code})");
            return false;
        }

        await _secrets.SetAsync(ApiKeyName, apiKey, ct).ConfigureAwait(true);
        await _secrets.SetAsync(ApiSecretName, apiSecret, ct).ConfigureAwait(true);
        _apiKey = apiKey;
        _apiSecret = apiSecret;
        RebuildClient();
        RaiseStateChanged();
        return true;
    }

    /// <summary>Requests a sign-in token and returns it plus the browser authorisation URL, or null on failure.</summary>
    public async Task<(string Token, Uri AuthUrl)?> BeginSignInAsync(CancellationToken ct)
    {
        if (_client is null)
        {
            return null;
        }

        try
        {
            string token = await _client.GetTokenAsync(ct).ConfigureAwait(true);
            var url = new Uri($"https://www.last.fm/api/auth/?api_key={Uri.EscapeDataString(_apiKey)}&token={Uri.EscapeDataString(token)}");
            return (token, url);
        }
        catch (LastFmException ex)
        {
            ShellLog.Write($"last.fm: getToken failed (code {ex.Code})");
            return null;
        }
    }

    /// <summary>Polls <c>auth.getSession</c> every 3 s for up to 5 minutes until the token is authorised, then stores the session.</summary>
    public async Task<bool> CompleteSignInAsync(string token, CancellationToken ct)
    {
        if (_client is null)
        {
            return false;
        }

        DateTimeOffset deadline = DateTimeOffset.UtcNow + PollTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                LastFmSession session = await _client.GetSessionAsync(token, ct).ConfigureAwait(true);
                await _secrets.SetAsync(SessionKeyName, session.Key, ct).ConfigureAwait(true);
                await _secrets.SetAsync(UserNameKey, session.Name, ct).ConfigureAwait(true);
                _sessionKey = session.Key;
                Username = session.Name;
                RaiseStateChanged();
                _ = RefreshUserInfoAsync(CancellationToken.None);
                ShellLog.Write("last.fm: signed in");
                return true;
            }
            catch (LastFmException ex) when (ex.IsTokenNotAuthorized)
            {
                await Task.Delay(PollInterval, ct).ConfigureAwait(true);
            }
            catch (LastFmException ex)
            {
                ShellLog.Write($"last.fm: getSession failed (code {ex.Code})");
                return false;
            }
        }

        return false;
    }

    /// <summary>Clears the stored session (does not remove the API key).</summary>
    public async Task SignOutAsync(CancellationToken ct)
    {
        await _secrets.DeleteAsync(SessionKeyName, ct).ConfigureAwait(true);
        await _secrets.DeleteAsync(UserNameKey, ct).ConfigureAwait(true);
        _sessionKey = null;
        Username = null;
        AvatarUrl = null;
        _loved.Clear();
        _qualifier.Reset();
        _lastNowPlayingKey = null;
        RaiseStateChanged();
        ShellLog.Write("last.fm: signed out");
    }

    /// <summary>Fetches the signed-in user's recent tracks for the settings page, or an empty list on failure.</summary>
    public async Task<IReadOnlyList<RecentTrack>> GetRecentTracksAsync(int limit, CancellationToken ct)
    {
        if (_client is null || !IsSignedIn || Username is null)
        {
            return Array.Empty<RecentTrack>();
        }

        try
        {
            return await _client.GetRecentTracksAsync(Username, _sessionKey!, limit, ct).ConfigureAwait(true);
        }
        catch (LastFmException ex)
        {
            ShellLog.Write($"last.fm: getRecentTracks failed (code {ex.Code})");
            return Array.Empty<RecentTrack>();
        }
    }

    /// <summary>Whether the given track is currently marked loved in this session.</summary>
    public bool IsLoved(TrackIdentity identity) => _loved.Contains(identity.Key);

    /// <summary>Loves or unloves a track. No-op when signed out.</summary>
    public async Task<bool> SetLovedAsync(TrackIdentity identity, bool loved, CancellationToken ct)
    {
        if (_client is null || !IsSignedIn || !identity.IsUsable)
        {
            return false;
        }

        try
        {
            if (loved)
            {
                await _client.LoveAsync(identity.Artist, identity.Track, _sessionKey!, ct).ConfigureAwait(true);
                _loved.Add(identity.Key);
            }
            else
            {
                await _client.UnloveAsync(identity.Artist, identity.Track, _sessionKey!, ct).ConfigureAwait(true);
                _loved.Remove(identity.Key);
            }

            RaiseStateChanged();
            return true;
        }
        catch (LastFmException ex)
        {
            ShellLog.Write($"last.fm: {(loved ? "love" : "unlove")} failed (code {ex.Code})");
            return false;
        }
    }

    /// <summary>The current active track's identity, or null when there is nothing usable playing.</summary>
    public TrackIdentity? CurrentTrack()
    {
        MediaSnapshot? snapshot = _coordinator.Active?.Current;
        return snapshot is null ? null : ToIdentity(snapshot);
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        if (_disposed)
        {
            return;
        }

        _ = PumpAsync();
    }

    private async Task PumpAsync()
    {
        // Drain any queued scrobbles whose backoff has elapsed, independently of the enabled toggle changing.
        await DrainQueueAsync().ConfigureAwait(true);

        if (!_settings.Enabled || !IsSignedIn || _client is null)
        {
            return;
        }

        MediaSnapshot? snapshot = _coordinator.Active?.Current;
        if (snapshot is null)
        {
            _qualifier.Reset();
            return;
        }

        TrackIdentity? identity = ToIdentity(snapshot);
        if (identity is null)
        {
            _qualifier.Reset();
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        bool playing = snapshot.State == PlaybackState.Playing;
        TimeSpan position = TimelineInterpolator.Displayed(snapshot.Timeline, snapshot.State, now);
        TimeSpan duration = snapshot.Timeline.Duration;

        // Now-playing on a new track start (only while playing, once per track).
        if (_settings.NowPlaying && playing && !string.Equals(identity.Key, _lastNowPlayingKey, StringComparison.Ordinal))
        {
            _lastNowPlayingKey = identity.Key;
            await SendNowPlayingAsync(identity, duration).ConfigureAwait(true);
        }

        // Qualification -> queue -> drain.
        if (_settings.Scrobble)
        {
            Scrobble? qualified = _qualifier.Update(identity, duration, position, playing, now);
            if (qualified is not null)
            {
                _queue.Enqueue(qualified);
                ShellLog.Write("last.fm: track qualified for scrobble");
                await DrainQueueAsync().ConfigureAwait(true);
            }
        }
    }

    private async Task SendNowPlayingAsync(TrackIdentity identity, TimeSpan duration)
    {
        if (_client is null)
        {
            return;
        }

        try
        {
            var scrobble = new Scrobble(identity, DateTimeOffset.UtcNow, duration > TimeSpan.Zero ? duration : null);
            await _client.UpdateNowPlayingAsync(scrobble, _sessionKey!, CancellationToken.None).ConfigureAwait(true);
        }
        catch (LastFmException ex)
        {
            ShellLog.Write($"last.fm: now-playing failed (code {ex.Code})");
        }
    }

    private async Task DrainQueueAsync()
    {
        if (_draining || _client is null || !IsSignedIn || !_settings.Enabled)
        {
            return;
        }

        _draining = true;
        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            IReadOnlyList<Scrobble> batch = _queue.DueBatch(now);
            if (batch.Count == 0)
            {
                return;
            }

            try
            {
                await _client.ScrobbleAsync(batch, _sessionKey!, CancellationToken.None).ConfigureAwait(true);
                _queue.MarkSuccess(batch);
                ShellLog.Write($"last.fm: scrobbled {batch.Count} track(s)");
            }
            catch (LastFmException ex)
            {
                _queue.MarkFailure(batch, now);
                ShellLog.Write($"last.fm: scrobble batch failed (code {ex.Code}); {_queue.Count} queued");
            }
        }
        finally
        {
            _draining = false;
        }
    }

    private async Task RefreshUserInfoAsync(CancellationToken ct)
    {
        if (_client is null || !IsSignedIn)
        {
            return;
        }

        try
        {
            LastFmUserInfo info = await _client.GetUserInfoAsync(_sessionKey!, ct).ConfigureAwait(true);
            _dispatcher.TryEnqueue(() =>
            {
                Username = info.Name;
                AvatarUrl = info.ImageUrl;
                RaiseStateChanged();
            });
        }
        catch (LastFmException ex)
        {
            ShellLog.Write($"last.fm: getUserInfo failed (code {ex.Code})");
        }
    }

    private async Task LoadKeysAsync(CancellationToken ct)
    {
        if (LastFmKeys.HasBuildKey)
        {
            _apiKey = LastFmKeys.BuildApiKey;
            _apiSecret = LastFmKeys.BuildSecret;
            return;
        }

        _apiKey = await _secrets.GetAsync(ApiKeyName, ct).ConfigureAwait(true) ?? string.Empty;
        _apiSecret = await _secrets.GetAsync(ApiSecretName, ct).ConfigureAwait(true) ?? string.Empty;
    }

    private void RebuildClient()
    {
        _client?.Dispose();
        _client = HasApiKey ? new LastFmClient(_handler, _apiKey, _apiSecret, ShellLog.Write) : null;
    }

    private static TrackIdentity? ToIdentity(MediaSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.Title) || snapshot.Artists.Count == 0)
        {
            return null;
        }

        string artist = string.Join(", ", snapshot.Artists);
        var identity = new TrackIdentity(artist, snapshot.Title, snapshot.Album);
        return identity.IsUsable ? identity : null;
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _client?.Dispose();
    }
}
