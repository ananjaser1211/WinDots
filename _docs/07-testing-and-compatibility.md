# 07 - Testing and compatibility

## Unit tests (`tests/WinDots.Core.Tests`, xunit)

| Area | Cases |
|---|---|
| `DrawerController` | click below threshold toggles; drag then release above `openThreshold` opens; fast flick below threshold opens; upward flick closes; rubber-band clamp; horizontal jitter ignored; reduced-motion path emits no spring |
| `VelocityTracker` | windowed average, stale sample eviction, sign |
| `TimelineInterpolator` | playing advances; paused holds; rate other than 1; clamp to end; pending seek window ignores stale events |
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
| YouTube video (Chrome 2026) | 2026-09-04 | yes, AUMID `Chrome` | title; channel absent; album null | PNG 12 KB | confirmed | not advertised for a single video | confirmed at target | not advertised | n/a | Reports `PlaybackRate` 0 even while playing; interpolator treats rate <= 0 as 1 |
| Windows Media Player (ZuneMusic) | 2026-09-04 | yes, packaged AUMID | title only for a local video; artists empty | BMP 3.6 MB frame grab | confirmed | not advertised for a single item | accepted; position advanced past target | shuffle state reported without shuffle capability; repeat advertised | n/a | Large artwork; keep the decode bound |
| VLC 3.0.23 | 2026-09-04 | **no session** | | | | | | | | VLC 3 does not publish SMTC on Windows; VLC 4 is expected to. Unsupported until then |
| Spotify for Windows | | not installed on the dev machine | | | | | | | | |
| YouTube Music (Edge) | | pending; Chromium behaviour expected to match Chrome | | | | | | | | |
| YouTube Music desktop client | | not installed on the dev machine | | | | | | | | |
| Firefox (any site) | | pending | | | | | | | | |

Observed on every run: PowerToys Peek leaves a metadata-less paused session behind after it has been used. `MediaSnapshot.HasMetadata` is false for it.

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
