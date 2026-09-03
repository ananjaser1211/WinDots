# 08 - Roadmap and milestones

Branch naming: `feat/<area>`, `fix/<area>`, `docs/<area>`. Every milestone ends with a Conventional Commit on `main` and its exit criteria ticked here with the verifying commit hash.

## Phase A - MVP

### M0 Foundation (done)
- Product docs, `_docs/`, `CLAUDE.md`, GPL-3.0 licence, ADRs 0001-0003. Commit `6ac2a3f`.

### M1 GSMTC capability spike - `feat/foundation` (done 2026-09-04)
1. Done: .NET SDK 10.0.400 installed and pinned in `global.json`.
2. Done: `WinDots.sln`, `Directory.Build.props`, `Directory.Packages.props` pinning Windows App SDK 2.4.0. Commit `bc82a83`.
3. Done: `WinDots.Core` contracts and records, plus `TimelineInterpolator` and `TimeFormat` with 16 unit tests. Commit `bc82a83`.
4. Done: `WinDots.Windows/Media/GsmtcSessionProvider` and `GsmtcSession` on a dedicated `MediaDispatcher` thread (WinRT objects fault with `RPC_E_WRONG_THREAD` when used across threads). Commits `bc82a83`, `41ca5f8`.
5. Done: `WinDots.App` packaged with `globalMediaControl`; `Diagnostics/SessionInspectorWindow` lists sessions, shows snapshot JSON and artwork, sends commands. Commit `c9dc119`.
6. Done: `tests/WinDots.TestPlayer` (controllable SMTC publisher driven over stdin) and `tests/WinDots.Windows.Tests` with an automated end-to-end test plus `RealPlayerProbe`. Commit `41ca5f8`.
7. Done: matrix filled for Chrome, Windows Media Player, VLC 3 (no session), and the test player. Spotify and the YouTube Music desktop client are not installed here; Edge and Firefox are pending.
- **Exit met**: Chrome and Windows Media Player were discovered and controlled independently of each other; the test player's arrival and removal are handled without restart; commands on a vanished session are rejected, not thrown.
- Findings feeding later milestones:
  - Stale metadata-less sessions exist (PowerToys Peek). The coordinator (M3) must rank `HasMetadata == false` sessions last.
  - Chrome reports playback rate 0 while playing; the adapter now stores 1.0 for any non-positive rate (`SessionQuality.NormalizeRate`) and the interpolator still guards independently.
  - Windows Media Player publishes multi-megabyte bitmap thumbnails; keep the byte bound and decode at reduced size (M3 artwork cache).
  - Session identity is AUMID plus ordinal. Fixed: wrappers are matched across enumerations by state fingerprint (`05-architecture.md`, "Session identity"), so a survivor keeps its ID when a duplicate leaves and `SystemCurrent` resolves to the right duplicate; covered by `DuplicateLeavingKeepsSurvivorIdentity`.
  - VLC 3 is unsupported by design of the platform, not a WinDots bug.

### M2 Drawer interaction - `feat/drawer-gesture`
1. `DrawerController`, `VelocityTracker` in Core with tests.
2. `HandleWindow` (per monitor) and `DrawerWindow` with the styles in `05-architecture.md`.
3. Composition translate + spring; reduced-motion path.
4. Shortcut, Escape, outside click, tray icon.
5. `MonitorService` with topology events.
- **Exit**: the M2 checklist in `07-testing-and-compatibility.md` passes at 100 % and 150 % DPI; ADR 0003 updated to Accepted or Superseded.

### M3 Functional media - `feat/media-ui`
1. `SessionCoordinator` + tests; `TimelineInterpolator` + tests.
2. Media page view: blob artwork, ring, metadata, seek, transport, chooser, lyrics shell.
3. Empty/unknown state identical to `Static.png`.
4. Artwork cache with limits.
- **Exit**: M3 checklist passes; matrix rows for Spotify, YouTube Music (Edge), VLC complete.

### M4 Windows polish - `feat/visual-polish`
1. Tokens, dark/light, acrylic + fallback, DWM corners.
2. `PaletteService` + tests; colour transitions.
3. Background blobs, ring/blob idle motion, reduced motion, high contrast, Narrator names.
4. Multi-monitor handles, display-change recovery.
- **Exit**: M4 checklist; 60 fps drawer on the dev machine.

### M5 Volume and settings - `feat/core-audio`, `feat/settings`
1. `CoreAudioSessionProvider` with confidence tiers + platform tests.
2. Volume row with confidence gating.
3. `SettingsStore`, migrator, settings window, startup task.
- **Exit**: volume never moves an unrelated app; corrupt settings recover.

### M6 Package and release - `feat/packaging`
1. Manifest, icons, versioning, dev-cert sideload instructions.
2. Diagnostics export, privacy doc, soak test.
3. Tag `v0.1.0` (with explicit user authorisation).
- **Exit**: install/upgrade/uninstall verified; acceptance criteria in `IMPLEMENTATION.md` section 8 met.

## Phase B - Dashboard tab

- **M7** Tab strip with two tabs, tab persistence, Ctrl+Tab.
- **M8** Clock, calendar, user card, resource rings (performance counters, `performance.sampleIntervalMs`).
- **M9** Mini media card reusing M3 components; weather card behind consent (ADR for provider).

## Phase C - Remaining tabs and integrations

- **M10** Performance tab. **M11** Weather tab. **M12** Lyrics providers. **M13** Visualiser. **M14** Last.fm. Then history, themes, favourites, focus modes per `IMPLEMENTATION.md` section 9.

## Execution order reminder

Never start the next milestone before the current one's exit criteria are recorded here as met.
