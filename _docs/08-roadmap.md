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

### M2 Drawer interaction - `feat/foundation` (done 2026-09-04)
1. Done: `DrawerController`, `VelocityTracker`, `SpringMotion` in Core with tests (commit `bb5f50d` and later).
2. Done: `HandleWindow` (per monitor) and `DrawerWindow`; two-window design validated, ADR 0003 Accepted with amendments (resize-based reveal, no manual `WS_*` edits, forced foreground).
3. Done: spring settle via `SpringMotion` on an 8 ms UI timer; reduced-motion 150 ms ramp.
4. Done: Win+Shift+M hotkey, Escape, click-outside, tray icon with menu, diagnostics command hook (`ShellMessageWindow`).
5. Done: `MonitorService` with topology events (commit `bc88694`); identical-layout events are ignored so handles are never duplicated.
- **Exit met**: `tests/scripts/Invoke-ShellCheck.ps1` passes on a dual-monitor setup at 100 % and 125 % (150 % not available on this machine); handles never activate; drawer takes foreground after open; cross-monitor move and settle interruption work.
- Open polish for M4: the handle is an opaque 200x12 strip (WinUI windows cannot be transparent yet); the drawer has no acrylic; hover growth is unverified by script.

### M3 Functional media - `feat/foundation` (code done 2026-09-04; visual QA pending)
1. Done: `SessionCoordinator` + `MediaOptions` + 30 tests; `SeekReconciliation`; `TimelineInterpolator` (commit `a83b7b3`).
2. Done: tokens (`Resources/Tokens.xaml`), controls `BlobArtwork`, `DottedProgressRing`, `TransportBar`, `SeekBar`, `PlayerChooser`, `LyricsPanel`; `MediaViewModel`; `MediaPage` hosted in `DrawerWindow`; diagnostics commands 7/8/9 (commit `7e18237`).
3. Done in code: empty state per `Static.png` (ring shown unfilled, "Unknown title/artist/album", chooser shows the system-current source).
4. Done: `ArtworkCache` (32 MB LRU + disk, single-flight) wired into the view-model.
- **Exit met (2026-09-04)**: `tests/scripts/Capture-Drawer.ps1` capture reviewed against `MediaPlayer.png`: blob artwork with dotted ring left, title/artist/album, seek with times, shuffle/previous/play pill/next/repeat, volume row, lyrics slot with "No lyrics found", player chooser pill. Controls were resized once (commit "fit media page controls") so all five transport buttons and the volume row fit in 720x300. Coordinator picks the playing test player over a paused Chrome; hook play/pause round-trips. Remaining M3 checklist items (livestream, advert metadata, two players paused+playing) are covered by the test player and Chrome sessions seen during the capture but not yet scripted.

### M4 Windows polish - `feat/foundation`
1. Tokens done (M3); acrylic + opaque fallback and DWM corners: to do (corners done in M2).
2. Done: `PaletteService` + `ColorMath` with 24 tests (commit pending push in this stage). Colour transitions and wiring into the view-model: to do.
3. Background blobs, ring/blob idle motion, reduced motion, high contrast, Narrator names: to do.
4. Multi-monitor handles done (M2); display-change recovery verified only by identical-layout guard: needs a real topology test.
- **Exit**: M4 checklist; 60 fps drawer on the dev machine.

### M5 Volume and settings - `feat/foundation`
1. Done: `CoreAudioSessionProvider` (CsWin32 COM on its own dispatcher, default-device re-attach) + `AudioMatchPolicy` with tests; platform tests against the test player, which now opens a real render session.
2. Done: volume row (mute glyph, slider, percentage, "shared" caption for Medium matches) shown only when the match is High, or Medium with `media.allowSharedVolume`; scroll wheel and Up/Down nudge by `media.volumeStepPercent`; M toggles mute; "Why is volume hidden?" in the lyrics overflow menu; diagnostics 11/12/13. Verified against the test player: High match, 25 % and mute round-trip in the log and the capture.
3. Done: `Settings` records, `JsonSettingsStore`, `SettingsMigrator`, `ShortcutParser` (Core); wired into `DrawerHost` (live apply), hotkey from `drawer.toggleShortcut` (on the dev machine Win+Shift+M is owned by PowerToys, so it logs 1409 and disables), `SettingsWindow`, startup task toggle.
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
