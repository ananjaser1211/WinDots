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
    double Progress { get; }   // current position, [0, 1] plus rubber-band
    double Target { get; }     // resting position (0 or 1) the view springs towards while settling
    void PointerDown(PointerSample s);
    void PointerMove(PointerSample s);
    void PointerUp(PointerSample s);
    void Toggle();
    void Dismiss(DismissReason reason);
    void AnimationCompleted();  // the view's spring settled; SettlingOpen/Closed -> Open/Closed
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

A stale session (`SessionQuality.IsStale`) is ranked below every non-stale session regardless of its score, and is chosen only when no non-stale candidate exists. Ties break by the most recent `CapturedAt`, then stably by session `Id`. `SelectionReason` reports the highest-weight rule that applied to the winner (`PinnedByUser`, `PreferredPlayer`, `Playing`, `RecentActivity`, `SystemCurrent`, `Paused`, or `OnlyCandidate` when no positive rule applied); it is exposed for diagnostics ("Pinned by user", "Playing", "Windows default").

`SessionCoordinator` (`WinDots.Core/Media`) implements `ISessionCoordinator`. It subscribes to the provider's `SessionsChanged` and `SystemCurrentChanged` and to each session's `Updated`, re-evaluating on any of them and reconciling per-session subscriptions as the set changes (handlers are never leaked; `Dispose` unsubscribes everything). `Pin(sessionId)` sticks until that session disappears from the unignored set, then reverts to automatic; `ClearPin` reverts immediately. `Candidates` is the unignored session set in ranked order, for the player chooser, with a `CandidatesChanged` event; `ActiveChanged` fires only when `Active` or `Reason` actually changes. State is guarded by a lock; events are raised on the calling thread (the provider callback thread, or the `Pin`/`ClearPin` caller) outside the lock, so consumers marshal as needed. `MediaOptions` carries `PreferredPlayer`, `IgnoredPlayers`, `PlayerAliases` (with an `AliasFor` helper matching an exact AUMID or a case-insensitive substring of the AUMID or display name), `TimelineTickMs` (500), and `RecentActivityWindow` (30 s).

## Drawer gesture (`WinDots.Core/Drawer`)

`DrawerController` implements `IDrawerController` with the state machine and thresholds from `03-ux-interaction-spec.md`; `DrawerOptions` carries the tunables (height, `dragThresholdPx`, `openThreshold`, `velocityThresholdPxPerS`, rubber-band factor, reduced motion) and `VelocityTracker` is the 60 ms windowed average (positive = downward, time taken only from sample timestamps). Decisions made in Core:

- A press is armed only in `Closed` or `Open`; a second press during an active gesture and pointer events while settling are ignored.
- Below `dragThresholdPx` the drawer does not move. Releasing there from `Closed` is a click (toggle); from the open drawer's top band it is a no-op.
- Progress during a drag is measured from the press origin's progress (0 from the handle, 1 from the open drawer), clamped at 0 and rubber-banded above 1.
- Release from the handle: open on downward velocity >= threshold or `progress >= openThreshold`; close on upward velocity >= threshold; otherwise nearer state. Release from the open drawer: same velocity rules, otherwise close only when upward travel reached `dragThresholdPx`.
- `Toggle` from a settling state reverses the target; `Toggle` or `Dismiss` during a drag cancels the gesture and later samples from that press are ignored. `Dismiss` is a no-op when already closed or closing.
- `Progress` is not changed by entering a settling state; the view animates from it to `Target` and calls `AnimationCompleted`, which snaps `Progress` to the target and enters `Open`/`Closed`. With `ReducedMotion` the settling states are skipped and the terminal state is entered at once so the view can cross-fade instead of springing.

## Timeline interpolation

```
displayed = Timeline.Position + (State == Playing ? (nowUtc - LastUpdatedUtc) * Rate : 0)
displayed = clamp(displayed, Start, End)
```

A `DispatcherQueueTimer` at `media.timelineTickMs` (default 500) drives the labels and ring while the drawer is open **and** state is Playing; it stops otherwise. GSMTC `TimelinePropertiesChanged` events replace the base values. A seek sets an optimistic base and marks `pendingSeekUntil = now + 2 s`; timeline events during that window that are more than 3 s from the target are ignored.

## Snapshot normalisation (`WinDots.Core/Media/SessionQuality`)

Pure policies the GSMTC adapter applies while building a `MediaSnapshot`, so consumers never see raw platform quirks:

| Value | Rule | Why |
|---|---|---|
| `Timeline.Rate` | anything that is not a finite positive number becomes `1.0` | Chromium reports `PlaybackRate` 0 while playing; some players report null |
| `Timeline.LastUpdated` | unset (default / epoch / FILETIME zero) or later than `CapturedAt` becomes `CapturedAt` | an unset stamp would project a playing track straight to its end; a future stamp (clock skew between the player and WinDots) would freeze it |
| `CapturedAt` | taken after the platform reads | never earlier than a stamp the platform wrote during the reads |
| `Capabilities.PlayPause` | set when the toggle flag **or** either direction (Play, Pause) is enabled | players advertise only the direction that currently applies; `TryTogglePlayPauseAsync` works with either |

`SessionQuality.IsStale(snapshot)` is true for a session with no title, artist, or album that is not Playing or Changing (PowerToys Peek leaves one behind). The coordinator ranks such sessions last; the adapter does not hide them, because a metadata-less session can still be the one the user wants to pause.

## Session identity (`GsmtcSessionProvider`)

A session ID is `<AUMID>#<ordinal>`. The platform offers no identity: `GetSessions()` and `GetCurrentSession()` return a **new COM object on every call** (verified on Windows 11 26200: neither managed reference identity nor the IUnknown pointer repeats), and a session whose player has exited keeps answering queries for a short while after it has left the enumeration. Identity is therefore inferred:

1. For each enumerated session, read a `SessionFingerprint` (timeline start/end/position/last-updated, playback status, shuffle, repeat) and compare it with the fingerprint each existing wrapper of the same AUMID reads from its own object at the same instant. Equal fingerprints mean the same underlying session: the wrapper, and its ID, survive. A mismatch is re-read once in case a playing session published between the two reads. Identical fingerprints among duplicates (two idle sessions with no timeline) resolve positionally.
2. Matching runs over the whole enumeration first; only then does each unmatched session get a new wrapper with the **lowest ordinal not held by a surviving wrapper** of that AUMID (or by a newcomer numbered just before it). Numbering after all matches matters because `GetSessions()` gives no ordering guarantee: a newcomer enumerated before a survivor would otherwise take the survivor's ordinal and produce two wrappers with the same ID.
3. A wrapper that matches nothing is disposed, even if its object still answers.

Consequences: a surviving session never changes ID when a duplicate (a second browser window) leaves; an ID is only reused after its previous holder is gone; `Sessions` keeps surviving sessions in their existing order and appends newcomers, so the list changes exactly when `SessionsChanged` is raised; `SystemCurrent` is resolved by the same fingerprint match, so a duplicate never steals the "current" marker from its sibling. Reconciles are serialised on the `MediaDispatcher`; a `SessionsChanged` that arrives mid-reconcile queues one more pass instead of interleaving.

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

## Reliability and performance rules

- Event-driven session observation with cancellation-aware async operations; coalesce rapid metadata events and discard stale artwork loads.
- Keep decoded artwork bounded by pixel dimensions and the cache budget; never block the UI thread on COM, file, network, or image work.
- Treat every player command as a request that can be rejected; reflect advertised capabilities in the UI.
- Re-enumerate cleanly after Explorer, audio service, display, or media-player restarts.
- Structured local logs with bounded rotation; diagnostics export omits media titles unless explicitly included.
- Budgets: idle CPU below 0.5 % closed and 1.5 % open, working set below 150 MB after an hour, 60 fps reveal, no unhandled exceptions in an eight-hour soak (measured per `07-testing-and-compatibility.md`).
