# 05 - Architecture

## Solution layout

```
WinDots.sln
Directory.Build.props            TFM net10.0-windows10.0.26100.0, nullable, warnings-as-errors, LangVersion latest
Directory.Packages.props         Central package versions (Microsoft.WindowsAppSDK 2.4.0, Microsoft.Windows.SDK.BuildTools, CsWin32, xunit)
src/
  WinDots.Core/                  Domain: models, contracts, policies, gesture + timeline math, palette, settings model
  WinDots.Windows/               Adapters: GSMTC, Core Audio, monitors, window styles, DWM, startup task, credential store
  WinDots.App/                   WinUI 3 packaged app: windows, views, view-models, tokens, composition, tray, settings UI
tests/
  WinDots.Core.Tests/            xunit, deterministic, no Windows APIs
  WinDots.Windows.Tests/         xunit, [Trait("Category","Platform")], skipped in CI without a desktop session
  WinDots.TestPlayer/            Tiny WinUI app that publishes a controllable SMTC session for manual/integration QA
```

Dependency direction: `App -> Windows -> Core`. `Core` references nothing but the BCL.

## Core contracts (`WinDots.Core/Contracts`)

```csharp
public interface IMediaSessionProvider : IAsyncDisposable
{
    IReadOnlyList<IMediaSession> Sessions { get; }
    IMediaSession? SystemCurrent { get; }
    event EventHandler<SessionsChangedEventArgs>? SessionsChanged;
    Task InitializeAsync(CancellationToken ct);
}

public interface IMediaSession
{
    string Id { get; }                       // stable per session lifetime
    string SourceAppId { get; }              // AUMID or exe name
    MediaSnapshot Current { get; }           // immutable
    event EventHandler<MediaSnapshot>? Updated;
    Task<CommandResult> TryPlayPauseAsync(CancellationToken ct);
    Task<CommandResult> TryNextAsync(CancellationToken ct);
    Task<CommandResult> TryPreviousAsync(CancellationToken ct);
    Task<CommandResult> TrySeekAsync(TimeSpan position, CancellationToken ct);
    Task<CommandResult> TrySetShuffleAsync(bool on, CancellationToken ct);
    Task<CommandResult> TrySetRepeatAsync(RepeatMode mode, CancellationToken ct);
    Task<ArtworkResult> LoadArtworkAsync(int maxPixels, CancellationToken ct);
}

public interface IAudioSessionProvider
{
    Task<AudioMatch> MatchAsync(string sourceAppId, CancellationToken ct);
    Task<bool> TrySetVolumeAsync(AudioMatch match, float level, CancellationToken ct);
    Task<bool> TrySetMuteAsync(AudioMatch match, bool mute, CancellationToken ct);
    event EventHandler<AudioSessionChangedEventArgs>? Changed;
}

public interface ISessionCoordinator
{
    IMediaSession? Active { get; }
    SelectionReason Reason { get; }
    void Pin(string sessionId);
    void ClearPin();
    event EventHandler? ActiveChanged;
}

public interface IDrawerController
{
    DrawerState State { get; }
    double Progress { get; }
    void PointerDown(PointerSample s);
    void PointerMove(PointerSample s);
    void PointerUp(PointerSample s);
    void Toggle();
    void Dismiss(DismissReason reason);
    event EventHandler<DrawerTransition>? Transition;
}

public interface IMonitorService
{
    IReadOnlyList<MonitorInfo> Monitors { get; }
    event EventHandler? TopologyChanged;
}

public interface IPaletteService { Palette FromArtwork(ReadOnlySpan<byte> bgra, int w, int h, bool dark); }
public interface IArtworkCache  { Task<CachedArtwork?> GetOrAddAsync(string key, Func<CancellationToken, Task<ArtworkResult>> loader, CancellationToken ct); }
public interface ISettingsStore  { Settings Current { get; } Task SaveAsync(Settings s, CancellationToken ct); event EventHandler<Settings>? Changed; }
public interface ISecretStore    { Task<string?> GetAsync(string key); Task SetAsync(string key, string value); Task DeleteAsync(string key); }
```

## Media snapshot

```csharp
public sealed record MediaSnapshot(
    string SessionId,
    string SourceAppId,
    string SourceDisplayName,
    string? Title,
    IReadOnlyList<string> Artists,
    string? Album,
    MediaKind Kind,                          // Music, Video, Unknown
    PlaybackState State,                     // Playing, Paused, Stopped, Changing, Unknown
    Capabilities Caps,                       // flags: PlayPause, Next, Previous, Seek, Shuffle, Repeat
    Timeline Timeline,                       // Start, End, Position, LastUpdatedUtc, Rate
    bool? Shuffle,
    RepeatMode? Repeat,
    string? ArtworkKey);                     // hash of thumbnail bytes, null when absent
```

## Session coordinator scoring

Evaluated on every `SessionsChanged` / `Updated`; the highest score wins, ties broken by most recent update.

| Rule | Score |
|---|---|
| User-pinned session | 1000 |
| Matches `media.preferredPlayer` | 400 |
| In `media.ignoredPlayers` | excluded |
| Playing | +300 |
| Metadata updated in the last 30 s | +100 |
| Windows current session | +50 |
| Paused | +20 |
| Stopped / unknown | 0 |

`SelectionReason` is exposed for diagnostics ("Pinned by user", "Playing", "Windows default").

## Timeline interpolation

```
displayed = Timeline.Position + (State == Playing ? (nowUtc - LastUpdatedUtc) * Rate : 0)
displayed = clamp(displayed, Start, End)
```

A `DispatcherQueueTimer` at `media.timelineTickMs` (default 500) drives the labels and ring while the drawer is open **and** state is Playing; it stops otherwise. GSMTC `TimelinePropertiesChanged` events replace the base values. A seek sets an optimistic base and marks `pendingSeekUntil = now + 2 s`; timeline events during that window that are more than 3 s from the target are ignored.

## Core Audio matching

1. Resolve `SourceAppId`: if it looks like an AUMID (contains `!`) resolve to the package family name and running process IDs via `PackageManager` / `GetPackagesByPackageFamily`; otherwise treat it as an executable name.
2. Enumerate `IAudioSessionEnumerator` on the default render endpoint; read `IAudioSessionControl2.GetProcessId` and `GetSessionIdentifier`.
3. Score:
   - `High`: exactly one session whose PID is in the candidate set, or all matching sessions belong to the same package.
   - `Medium`: multiple matching sessions across processes (browsers). Volume applies to all of them together.
   - `None`: no candidate PIDs, or the session's process is a shared host (`RuntimeBroker`, `audiodg`).
4. Volume UI is shown for `High`; shown with a "shared with other tabs" note for `Medium` only when `media.allowSharedVolume` is true; hidden for `None`.
5. Re-evaluate on `IAudioSessionNotification.OnSessionCreated` and on session `Updated`.

## Windowing (see ADR 0003)

- **Handle window** per monitor: Win32 `HWND` created through `AppWindow`, styles `WS_POPUP | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST | WS_EX_LAYERED`, `SetWindowPos` on topology change. Pointer capture via `SetCapture` while dragging.
- **Drawer window**: one WinUI `Window` reused across monitors, `WS_EX_TOOLWINDOW | WS_EX_TOPMOST`, no caption, `ExtendsContentIntoTitleBar`, size/position via `AppWindow.MoveAndResize`, DWM rounded corners.
- Both use `DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2` (manifest).
- Outside-click detection: `WM_ACTIVATEAPP` deactivate plus a `WinEventHook(EVENT_SYSTEM_FOREGROUND)` scoped to our own process; no global input hook.

## Threading

- `WinDots.Windows` adapters marshal every GSMTC/COM callback onto a dedicated `MediaDispatcher` (single-threaded `TaskScheduler`) and publish immutable snapshots.
- View-models subscribe and hop to the UI `DispatcherQueue`.
- Artwork decode runs on the thread pool with a `CancellationTokenSource` per session; a newer `ArtworkKey` cancels the older load.
- All COM objects are released via `Marshal.FinalReleaseComObject` in `Dispose`; the app never calls COM from finalizers.

## Persistence

- Settings: `%LOCALAPPDATA%\WinDots\settings.json`, atomic write (temp file then `File.Move` overwrite), schema version field, backup of the last good file.
- Artwork cache: `%LOCALAPPDATA%\WinDots\cache\artwork\` keyed by hash, 32 MB LRU, 30-day expiry.
- Logs: `%LOCALAPPDATA%\WinDots\logs\`, rolling 5 x 2 MB, titles/artists redacted unless `diagnostics.includeMediaText`.
- Secrets: Windows Credential Manager via `ISecretStore` (Phase C).

## Packaging

- MSIX with `rescap:Capability Name="globalMediaControl"`, `runFullTrust`, a `StartupTask` extension, and per-monitor DPI v2 in the app manifest.
- Self-contained Windows App SDK is **off** (framework-dependent) to keep the package small; revisit if sideload friction appears.
