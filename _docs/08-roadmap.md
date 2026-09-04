# 08 - Roadmap and milestones

Branch naming: `feat/<area>`, `fix/<area>`, `docs/<area>`. Every milestone ends with a Conventional Commit on `main` and its exit criteria ticked here with the verifying commit hash.

## Phase A - MVP

### M0 Foundation (done)
- Product docs, `_docs/`, GPL-3.0 licence, ADRs 0001-0003. Commit `6ac2a3f`.

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
- Open polish for M4 (now resolved in M4): the handle was an opaque 200x12 strip; it is now the pill itself, clipped to a stadium region with transparent corners. The drawer gained acrylic. Hover growth remains unverified by script.

### M3 Functional media - `feat/foundation` (code done 2026-09-04; visual QA pending)
1. Done: `SessionCoordinator` + `MediaOptions` + 30 tests; `SeekReconciliation`; `TimelineInterpolator` (commit `a83b7b3`).
2. Done: tokens (`Resources/Tokens.xaml`), controls `BlobArtwork`, `DottedProgressRing`, `TransportBar`, `SeekBar`, `PlayerChooser`, `LyricsPanel`; `MediaViewModel`; `MediaPage` hosted in `DrawerWindow`; diagnostics commands 7/8/9 (commit `7e18237`).
3. Done in code: empty state per `Static.png` (ring shown unfilled, "Unknown title/artist/album", chooser shows the system-current source).
4. Done: `ArtworkCache` (32 MB LRU + disk, single-flight) wired into the view-model.
- **Exit met (2026-09-04)**: `tests/scripts/Capture-Drawer.ps1` capture reviewed against `MediaPlayer.png`: blob artwork with dotted ring left, title/artist/album, seek with times, shuffle/previous/play pill/next/repeat, volume row, lyrics slot with "No lyrics found", player chooser pill. Controls were resized once (commit "fit media page controls") so all five transport buttons and the volume row fit in 720x300. Coordinator picks the playing test player over a paused Chrome; hook play/pause round-trips. Remaining M3 checklist items (livestream, advert metadata, two players paused+playing) are covered by the test player and Chrome sessions seen during the capture but not yet scripted.

### M4 Windows polish - `feat/foundation`
1. Done: tokens (M3); acrylic (`DesktopAcrylicController`) with opaque `Surface` fallback (advanced-effects off / battery saver / Remote Desktop / high contrast / failure) and DWM round corners.
2. Done: `PaletteService` + `ColorMath` with 24 tests; artwork/fixed/fallback palettes wired into `MediaViewModel` (accent, on-accent, accent-container, blob-tint brushes) with 400 ms colour transitions.
3. Done: background blobs with phase-offset idle drift, artwork blob phase drift, reduced-motion and high-contrast suppression, high-contrast ThemeDictionary + ring/blob strokes, Narrator names on every interactive control, a hidden Polite live region announcing play/pause, and a text-scale/width two-row reflow.
4. Done: the collapsed handle is now the pill itself — the window is sized to the visual (160 x 6, 200 x 8 hover) and clipped to a stadium region via `SetWindowRgn`, corners transparent; hover grows the window and re-applies the region.
5. Multi-monitor handles done (M2); display-change recovery verified only by identical-layout guard: needs a real topology test.
- Deferred: 40 px blob blur (flat soft ellipses for now); a real topology (display-change) test; 60 fps drawer measurement on the dev machine.
- **Exit**: M4 checklist met except the deferred items above; `Invoke-ShellCheck.ps1` handle detection updated to the pill size.

### M5 Volume and settings - `feat/foundation`
1. Done: `CoreAudioSessionProvider` (CsWin32 COM on its own dispatcher, default-device re-attach) + `AudioMatchPolicy` with tests; platform tests against the test player, which now opens a real render session.
2. Done: volume row (mute glyph, slider, percentage, "shared" caption for Medium matches) shown only when the match is High, or Medium with `media.allowSharedVolume`; scroll wheel and Up/Down nudge by `media.volumeStepPercent`; M toggles mute; "Why is volume hidden?" in the lyrics overflow menu; diagnostics 11/12/13. Verified against the test player: High match, 25 % and mute round-trip in the log and the capture.
3. Done: `Settings` records, `JsonSettingsStore`, `SettingsMigrator`, `ShortcutParser` (Core); wired into `DrawerHost` (live apply), hotkey from `drawer.toggleShortcut` (on the dev machine Win+Shift+M is owned by PowerToys, so it logs 1409 and disables), `SettingsWindow`, startup task toggle.
- **Exit**: volume never moves an unrelated app; corrupt settings recover.

### M6 Package and release - `feat/packaging`
1. Manifest, icons, versioning, dev-cert sideload instructions.
2. Diagnostics export, privacy doc, soak test.
3. Tag `v0.1.0` (with explicit user authorisation).
- **Exit**: install/upgrade/uninstall verified; the MVP acceptance criteria at the end of this file met.

## Phase B - Dashboard tab

- **M7** Tab strip with two tabs, tab persistence, Ctrl+Tab. — BUILT (2026-09-05): four-tab strip (Dashboard/Media/Performance/Weather, `DrawerTabStrip`), `Settings.SelectedTab` persistence (default Media), Ctrl+Tab / Ctrl+Shift+Tab cycling, per-tab drawer sizing (Media area unchanged at 344 px + 72 px strip), ease-out height tween on tab switch. Performance/Weather are placeholder pages.
- **M8** Clock, calendar, user card, resource rings (performance counters, `performance.sampleIntervalMs`). — BUILT (2026-09-05): `DashboardPage` widgets from the Core `Dashboard/` logic + `ISystemMetricsProvider`; stacked clock (1 s timer), month calendar (today highlighted, prev/next), user card (account picture + player chip + uptime), three 270° resource rings (CPU/mem/disk) sampled at `performance.ClampedSampleIntervalMs` off the UI thread; timers gated on drawer-open + Dashboard-visible.
- **M9** Mini media card reusing M3 components; weather card behind consent (ADR for provider). — BUILT (2026-09-05): mini now-playing card reuses `DottedProgressRing` + transport bound to the shared `MediaViewModel`; weather card is a consent-gated placeholder (`WeatherSettings.ConsentGranted`, no provider yet — ADR pending). **Pending on-device verification** (workstation locked): confirm tab switching, dashboard widgets, live rings, and that the media reveal is unregressed.

## Phase C - Remaining tabs and integrations

- **M10** Performance tab. **M11** Weather tab. **M12** Lyrics providers. **M13** Visualiser. **M14** Last.fm. Then the E7 items in `10-enhancement-plan.md`.

## Execution order reminder

Never start the next milestone before the current one's exit criteria are recorded here as met.

## MVP acceptance criteria (checked at M6)

- Interaction: handle opens by click and drag; drag is continuous and completes on distance or velocity; dismiss by upward drag, outside click, Escape; handle absent from taskbar and Alt+Tab; multi-monitor placement correct at mixed scales.
- Media: compatible players appear automatically; active player is sensible and overridable; metadata and artwork update on track change; play/pause, previous, next, seek work when advertised; multiple sessions browsable; malformed metadata handled.
- Quality: no continuous high-frequency polling; budgets in `05-architecture.md` met; essential controls keyboard-accessible with accessibility names.

## Key risks and mitigations

| Risk | Mitigation |
|---|---|
| Player exposes incomplete session data | Capability-aware UI, aliases, compatibility matrix |
| Browser audio cannot be mapped to one tab | Conservative confidence threshold; hide volume instead of guessing |
| Handle interferes with application chrome | Narrow hit target, per-monitor disable, configurable position and shortcut |
| Topmost UI conflicts with games | Full-screen suppression and keyboard fallback |
| Restricted media capability complicates packaging | Proven in M1; documented sideloading |
| Malformed or oversized artwork | Bounded streams, decode limits, cancellation, cache quotas |
| Visual effects consume power | Composition animations, battery-saver and reduced-motion modes |
| Duplicate scrobbles | Deterministic track identity, qualification state machine, idempotent queue |
| Scope creep before the core is stable | Milestone exit criteria; integrations after the MVP |
