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

Require an interactive desktop and the `WinDots.TestPlayer` app running.

- GSMTC provider sees the test player appear, updates metadata, and sees it disappear.
- Commands: play/pause round trip; seek accepted; unsupported command returns `CommandResult.Unsupported`.
- Artwork: oversized image is downscaled; truncated stream returns `ArtworkResult.Failed` without throwing.
- Core Audio: the test player's session matches `High`; a second instance drops to `Medium`; unknown AUMID yields `None`.
- Monitor service: enumerates at least one monitor with a non-zero work area.

## Manual compatibility matrix

Record results per release in this table (copy and fill).

| Player | Discovered | Metadata | Artwork | Play/Pause | Prev/Next | Seek | Shuffle/Repeat | Volume match | Notes |
|---|---|---|---|---|---|---|---|---|---|
| Spotify for Windows | | | | | | | | | |
| YouTube Music (Edge) | | | | | | | | | |
| YouTube Music (Chrome) | | | | | | | | | |
| YouTube video (Chrome), 2026-09-04 inspector | yes | title + channel as artist | not yet checked | advertised | advertised | advertised | not advertised | n/a (M5) | Stale PowerToys Peek session also listed |
| YouTube Music desktop client | | | | | | | | | |
| VLC | | | | | | | | | |
| Windows Media Player | | | | | | | | | |
| Firefox (any site) | | | | | | | | | |
| WinDots.TestPlayer | | | | | | | | | |

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
