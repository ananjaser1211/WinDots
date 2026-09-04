# 07 - Testing and compatibility

## Unit tests (`tests/WinDots.Core.Tests`, xunit)

| Area | Cases |
|---|---|
| `DrawerController` | click below threshold toggles; drag then release above `openThreshold` opens; fast flick below threshold opens; upward flick closes; rubber-band clamp; horizontal jitter ignored; reduced-motion path emits no spring; open-drawer top-band drag closes on upward travel past the threshold and stays open on a click or downward pull; toggle/dismiss/animation-completed transitions and their no-op cases (implemented, `Drawer/DrawerControllerTests`) |
| `VelocityTracker` | windowed average, stale sample eviction, sign, horizontal motion ignored, clock going backwards (implemented, `Drawer/VelocityTrackerTests`) |
| `TimelineInterpolator` | playing advances; paused holds; rate other than 1; clamp to end; pending seek window ignores stale events |
| `SessionQuality` | metadata-less idle session is stale, active or titled one is not; invalid rates (null, 0, negative, NaN, infinity) become 1; future or unset `LastUpdated` becomes `CapturedAt` (implemented, `Media/SessionQualityTests`) |
| `SessionCoordinator` | every scoring table row; pin sticks until removal; ignored players excluded; tie-break by recency |
| `BlobGeometry` | deterministic for a seed; closed path; amplitude 0 is a circle |
| `PaletteService` | fixed artwork fixtures produce an accent with >= 4.5:1 contrast; fallback on a transparent image |
| `SettingsMigrator` | each version step; corrupt file recovery; unknown keys preserved |
| `ShortcutParser` | valid/invalid chords |
| `TimeFormat` | `m:ss`, `h:mm:ss`, negative/unknown |

Run one: `dotnet test tests/WinDots.Core.Tests --filter "FullyQualifiedName~DrawerControllerTests.FlickOpens"`.

## Platform tests (`tests/WinDots.Windows.Tests`, `Category=Platform`)

Require an interactive desktop. The test host launches `WinDots.TestPlayer.exe` (built alongside the tests) itself.

```powershell
dotnet test tests/WinDots.Windows.Tests -p:Platform=x64
```

Implemented in `GsmtcSessionProviderTests`:

- The provider sees the test player appear with title, artist, album, duration, capabilities, and an artwork key.
- Artwork loads; an undersized byte limit yields `ArtworkResult.Failed` without throwing.
- Next, play/pause, seek, shuffle, and repeat round-trip: the command is accepted, the player logs the request, and the new state flows back into the snapshot.
- Quitting the player removes the session; a command on the vanished session is rejected, not thrown.
- `DuplicateLeavingKeepsSurvivorIdentity`: two test players share one AUMID and get `#0` and `#1`; when `#0` quits, `#1` keeps its wrapper instance and ID (no renumbering); a third player then takes the free `#0`, and every ID of that AUMID is unique.

`GsmtcSessionOrdinalTests` (same project, no desktop needed) pins the pure numbering rule `GsmtcSessionProvider.NextOrdinal`: lowest free ordinal regardless of survivor order, freed ordinals reused, malformed IDs ignored, and newcomers numbered after all survivors never collide with a survivor's ordinal (the order-dependent case the platform test cannot force).

Planned: Core Audio matching tiers (M5) and monitor enumeration (M2).

### Real-player probe

`RealPlayerProbe` drives any running player and prints its snapshot, artwork result, and command outcomes:

```powershell
$env:WINDOTS_PROBE_APP = "Chrome"     # substring of the player's AUMID; see the Sessions line in the output
dotnet test tests/WinDots.Windows.Tests -p:Platform=x64 --filter "FullyQualifiedName~RealPlayerProbe" --logger "console;verbosity=detailed"
```

It toggles play/pause twice and seeks five seconds forward, so expect the player to react.

## Compatibility matrix

Columns record what the session advertised and what the probe confirmed. "n/a" means not yet implemented in WinDots.

| Player | Date | Discovered | Metadata | Artwork | Play/Pause | Prev/Next | Seek | Shuffle/Repeat | Volume match | Notes |
|---|---|---|---|---|---|---|---|---|---|---|
| WinDots.TestPlayer | 2026-09-04 | yes | title, artist, album, track no. | BMP, loads | confirmed | confirmed | confirmed | confirmed | n/a | Automated in `GsmtcSessionProviderTests` |
| YouTube video (Chrome 2026) | 2026-09-04 | yes, AUMID `Chrome` | title; channel absent; album null | PNG 12 KB | confirmed | not advertised for a single video | confirmed at target | not advertised | n/a | Reports `PlaybackRate` 0 even while playing; the adapter stores 1.0 (`SessionQuality.NormalizeRate`). Re-probed 2026-09-04 after the identity fix: toggle, seek at target, `SystemCurrent` = `Chrome#0`, PNG 24 KB. One run saw Chrome drop and recreate its session on the first toggle after 20 min idle (old object stopped answering); the provider replaces the wrapper on the resulting SessionsChanged |
| Windows Media Player (ZuneMusic) | 2026-09-04 | yes, packaged AUMID | title only for a local video; artists empty | BMP 3.6 MB frame grab | confirmed | not advertised for a single item | accepted; position advanced past target | shuffle state reported without shuffle capability; repeat advertised | n/a | Large artwork; keep the decode bound |
| VLC 3.0.23 | 2026-09-04 | **no session** | | | | | | | | VLC 3 does not publish SMTC on Windows; VLC 4 is expected to. Unsupported until then |
| Spotify for Windows | | not installed on the dev machine | | | | | | | | |
| YouTube Music (Edge) | | pending; Chromium behaviour expected to match Chrome | | | | | | | | |
| YouTube Music desktop client | | not installed on the dev machine | | | | | | | | |
| Firefox (any site) | | pending | | | | | | | | |

Observed on every run: PowerToys Peek leaves a metadata-less paused session behind after it has been used. `MediaSnapshot.HasMetadata` is false for it.

Platform behaviour established while fixing session identity (2026-09-04, Windows 11 26200): `GetSessions()` returns a new COM object per call for the same session, so wrappers are matched by state fingerprint (`_docs/05-architecture.md`, "Session identity"); an exited player's session object keeps answering `GetPlaybackInfo` at the moment the removal event fires and only starts throwing afterwards, so liveness probing cannot replace matching.

## Shell check (on device, no input injection)

Never test the shell by injecting global keyboard or mouse input; it lands in whatever window is foreground (it has stopped the developer's own terminal session). The app exposes a diagnostics hook instead: post `WM_APP+2` (`0x8002`) to the hidden window of class `WinDots.ShellMessageWindow` with `wParam` = command (1 toggle at cursor, 2 toggle on monitor index `lParam`, 3 dismiss, 4 inspector, 5 quit, 6 dump state) and read the log at `%LOCALAPPDATA%\Packages\<pfn>\LocalState\logs\shell.log`.

```powershell
.\tests\scripts\Invoke-ShellCheck.ps1 -Launch   # registers the Debug build, launches, drives, quits
```

It asserts one process, one handle per monitor, open/close via the hook, foreground ownership after open, cross-monitor move, settle interruption, and Quit. If pixels are needed, capture only the drawer's own rectangle with `Graphics.CopyFromScreen`; `PrintWindow` renders WinUI content black.

## Manual checklist per milestone

- **M2 drawer**: open/close 50 times via click, drag, shortcut, Escape, outside click; no focus theft while collapsed; handle invisible to Alt+Tab; 125 % and 150 % DPI; taskbar top/left/auto-hide.
- **M3 media**: track change updates within 1 s; player exit while drawer open; two players paused+playing; livestream (no duration); podcast; advert metadata.
- **M4 polish**: theme switch live; monitor unplug/replug; reduced motion; high contrast; Narrator reads every control.
- **M5 volume**: changing WinDots volume never moves another app's slider in the Volume Mixer.
- **M6 package**: clean install, upgrade over the previous version, startup task on/off, uninstall leaves no scheduled tasks.

## Performance budgets

| Metric | Target | How measured |
|---|---|---|
| Idle CPU (drawer closed) | < 0.5 % | Task Manager 5-min average |
| Idle CPU (drawer open, playing) | < 1.5 % | same |
| Working set after 1 h | < 150 MB | same |
| Drawer animation | 60 fps, never more than 2 dropped frames in a row | Composition frame stats / PIX |
| Open latency (shortcut to first frame) | < 120 ms | ETW trace |
| Soak | 8 h with a script cycling players; zero unhandled exceptions | log review |
