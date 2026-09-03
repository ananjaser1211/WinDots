# WinDots implementation plan

## 1. Objective

Build a polished, native Windows media drawer that can be pulled down from the top-center of the screen and can control any compatible Windows media session. The product should reproduce the qualities that make Caelestia's player compelling—immediacy, fluid motion, rich artwork, dynamic colour, multiple-player awareness, and a desktop-shell feeling—without porting the Linux shell or depending on WSL.

### Success statement

A user starts music in Spotify, YouTube Music, a browser, VLC, or another compatible player; pulls the top-center handle downward; immediately sees the correct artwork and metadata; controls playback, seeking, and volume; switches players if needed; and dismisses the drawer with a natural upward gesture.

## 2. Scope boundaries

### In scope

- Windows 11 desktop application.
- Native Windows media-session discovery and playback control.
- Native Windows application-audio control.
- Top-edge pull-down interaction and keyboard invocation.
- Multiple monitors and per-monitor DPI.
- Acrylic, dynamic colour, animation, theming, and accessibility.
- Local configuration, optional local history, and user-approved online integrations.
- MSIX packaging and start-with-Windows integration.

### Out of scope

- WSL, MPRIS, D-Bus, Hyprland, Quickshell, VNC, RDP, or a Linux companion process.
- Scraping media application windows or private application APIs.
- Downloading or redistributing copyrighted audio, artwork, or lyrics.
- Bypassing DRM or player restrictions.
- Replacing the Windows shell, taskbar, Start menu, or window manager.
- Guaranteeing commands that an upstream media session marks unsupported.

## 3. User experience specification

### 3.1 Collapsed handle

- Default logical size: approximately 160 by 6 pixels.
- Positioned at the horizontal centre of each enabled monitor's top edge.
- Topmost, borderless, and absent from Alt+Tab and the taskbar.
- Does not activate the application on hover.
- Provides a slightly larger configurable pointer hit target without reserving a broad strip across the display.
- Can be disabled per monitor or hidden during configured full-screen scenarios.
- Visually expands on hover or touch proximity.

### 3.2 Pull-down gesture

- Pointer-down captures the pointer within the handle window.
- Vertical travel maps directly to drawer reveal progress with light resistance near the maximum extent.
- Horizontal jitter is tolerated and does not cancel an otherwise vertical gesture.
- Release commits to open when distance or downward velocity crosses a configurable threshold.
- Otherwise, the drawer springs closed.
- Upward dragging closes the expanded drawer.
- Mouse, pen, and touch paths share one state machine.
- A click on the handle toggles the drawer for users who do not want to drag.

### 3.3 Expanded drawer

Initial target size is approximately 720 by 300 logical pixels, responsive to display size and scale.

Content includes:

- Large album artwork.
- Artist, track title, album, and source player.
- Elapsed time, duration, and seek bar.
- Previous, play/pause, and next controls.
- Volume and mute when an audio session can be matched confidently.
- Player chooser when multiple sessions are available.
- Clear empty state when no session is available.
- Status feedback for unsupported or rejected controls.

Dismissal paths:

- Upward swipe.
- Escape.
- Click outside.
- Configurable inactivity timeout.
- Optional automatic dismissal after a command.

### 3.4 Visual direction

- Desktop Acrylic foundation with an opaque fallback.
- Rounded or softly shaped container with DWM-compatible clipping.
- Palette derived from artwork, constrained for contrast and readability.
- Cross-fade and scale transitions when artwork changes.
- Layered, slowly moving background shapes.
- Spring motion for the drawer and short easing for control feedback.
- Dark, light, and automatic modes.
- Reduced motion, higher contrast, and transparency-disabled alternatives.

The implementation may be inspired by Caelestia's interaction and visual language, but its source and assets must not be copied.

## 4. Platform and technology decisions

### 4.1 Primary stack

- C# with .NET 10.
- WinUI 3.
- Stable Windows App SDK version selected and pinned at project scaffolding time.
- Windows Composition for high-frequency transforms and opacity animations.
- Win32 interop for specialised window styles and DWM behaviour.
- MSIX packaging.

### 4.2 Why this stack

- Direct access to Windows Runtime media APIs.
- Native window handles and AppWindow integration.
- Native Desktop Acrylic and Windows theme support.
- Good interoperability with Core Audio COM interfaces.
- Composition-thread animations can remain fluid even if media metadata work is delayed.
- Packaging can declare the restricted `globalMediaControl` capability explicitly.

### 4.3 Development prerequisites

Current workstation findings:

- Git is installed.
- .NET runtimes through 10.0 are installed.
- No .NET SDK is currently installed.
- Visual Studio Community 2022 and Build Tools are installed.
- Windows SDK 10.0.26100 is present.

Before scaffolding, install the .NET 10 SDK and verify the WinUI/Windows App SDK build workload. Keep acquisition changes outside source control and document the exact versions in the repository.

## 5. Architecture

### 5.1 Proposed solution layout

```text
WinDots.sln
src/
  WinDots.App/                 WinUI startup, views, resources, tray and packaging hooks
  WinDots.Core/                Domain models, policies and provider abstractions
  WinDots.Windows/             GSMTC, Core Audio, windowing, DWM and startup integrations
tests/
  WinDots.Core.Tests/          Deterministic unit tests
  WinDots.Windows.Tests/       Platform integration tests where practical
  WinDots.TestPlayer/          Controlled fake media publisher for manual/integration QA
docs/
  decisions/                   Architecture decision records
  test-matrix.md               Player and environment compatibility results
  privacy.md                   Data storage and integration behaviour
```

Keep the core domain free of WinUI types. Platform APIs are adapted into testable interfaces.

### 5.2 Core contracts

Initial abstractions:

- `IMediaSessionProvider`: observes available media sessions.
- `IMediaSession`: exposes normalized metadata, timeline, capabilities, state, and commands.
- `IAudioSessionProvider`: discovers application audio sessions and controls volume/mute.
- `ISessionCoordinator`: scores, selects, pins, and switches active sessions.
- `IArtworkService`: loads, caches, validates, and downscales artwork.
- `IPaletteService`: produces accessible colour tokens from artwork.
- `IDrawerController`: owns the reveal-state machine independent of view rendering.
- `IMonitorService`: tracks display bounds, work areas, scale, and topology changes.
- `ISettingsStore`: persists versioned user configuration atomically.
- `ISecretStore`: stores integration credentials with Windows Credential Manager or DPAPI.
- `IListeningIntegration`: optional now-playing, scrobble, history, or recommendation provider.

### 5.3 Normalized media model

Each media snapshot contains:

- Stable session identifier.
- Source application user model ID and friendly alias.
- Title, artist list, album, track number, and media type.
- Artwork reference and decoded thumbnail.
- Playback state and supported controls.
- Timeline start, end, current position, last update time, and playback rate.
- Shuffle and repeat state when exposed.
- Confidence-scored association with a Windows audio session.
- Optional external identifiers such as Last.fm or MusicBrainz matches.

Snapshots are immutable. Commands operate through the live session object and refresh state after completion.

## 6. Windows integrations

### 6.1 Global media sessions

Use `GlobalSystemMediaTransportControlsSessionManager` to:

- Request media-control access.
- Enumerate sessions.
- Observe current-session and session-list changes.
- Read media, playback, and timeline properties.
- Subscribe to metadata, playback, and timeline events.
- Execute only capabilities advertised by a session.

Avoid aggressive polling. Timeline progress should be interpolated locally from a timestamped snapshot and reconciled when a new event arrives.

### 6.2 Active-session policy

Windows' current session is the default, but the coordinator adds deterministic policy:

1. User-pinned session.
2. Most recently user-selected session.
3. Currently playing session with recent metadata activity.
4. Windows-designated current session.
5. Most recently paused session.

Settings can define aliases, ignored players, and preferred players. A manual selection remains sticky until that session disappears or the user returns to automatic mode.

### 6.3 Core Audio

GSMTC does not provide player volume. Enumerate Core Audio sessions and use `IAudioSessionControl2` plus `ISimpleAudioVolume`.

Matching strategy:

- Resolve the GSMTC source application to package identity and/or process candidates.
- Prefer an exact process match.
- Handle multi-process and shared browser sessions explicitly.
- Assign a confidence score and expose volume only above a conservative threshold.
- Never change master or unrelated application volume as a silent fallback.

If a safe match cannot be made, hide or disable the player-volume control and explain why in diagnostics.

### 6.4 Windowing

Use a small collapsed topmost tool window and an expanded drawer window or a carefully resized single window, depending on prototype results.

Requirements:

- No title bar, resize border, Alt+Tab entry, or taskbar entry.
- Non-activating while collapsed.
- Keyboard-accessible and focusable while expanded.
- Always-on-top policy controlled by settings.
- Correct work-area placement around taskbars and display cut-outs.
- Per-monitor DPI awareness v2.
- Restore placement after topology, orientation, taskbar, or scale changes.

A low-level global mouse hook is not part of the default design. It may be considered only if the narrow handle cannot provide acceptable interaction, and it must never suppress unrelated input.

## 7. MVP delivery milestones

### Milestone 0: repository foundation

Deliverables:

- Standalone Git repository on `main`.
- README, implementation plan, and contributor/agent rules.
- Licence decision recorded before importing dependencies or distributable assets.
- Solution layout and dependency policy documented.

Exit criteria:

- Repository is clean.
- Initial documentation commit exists.

### Milestone 1: GSMTC capability spike

Build the smallest packaged executable capable of:

- Requesting `globalMediaControl` access.
- Listing all sessions and their source IDs.
- Printing current metadata, playback state, timeline, and capabilities.
- Loading artwork safely.
- Sending play/pause, next, previous, and seek commands.
- Recording compatibility findings for Spotify, Edge/Chrome YouTube Music, VLC, and another native player.

Exit criteria:

- At least two independent players are discovered and controlled.
- Session arrival, removal, and track changes work without restarting WinDots.
- Unsupported controls fail safely.

### Milestone 2: drawer interaction prototype

- Create the top-center handle.
- Implement pointer capture and drag progress.
- Implement open/close thresholds and spring completion.
- Add keyboard toggle and Escape dismissal.
- Validate no-activation behaviour and click interference.
- Add primary-monitor DPI correctness.

Exit criteria:

- The drawer can be opened and dismissed repeatedly without focus bugs.
- Dragging remains smooth under metadata updates.
- The handle does not block a broad top-screen area.

### Milestone 3: functional media MVP

- Bind normalized media state to the UI.
- Add artwork, metadata, source identity, progress, and control buttons.
- Add seeking with optimistic UI and reconciliation.
- Add session chooser and automatic-selection rules.
- Add empty, loading, unavailable, and error states.
- Add artwork caching with size and lifetime limits.

Exit criteria:

- Core media functions work across the agreed player matrix.
- A disappearing player cannot crash or strand the drawer.
- Progress remains accurate through play, pause, seek, and track transition.

### Milestone 4: Windows-native polish

- Desktop Acrylic and opaque fallback.
- Artwork palette extraction with contrast validation.
- Track and player transition animation.
- DWM corners and composition-based drawer animation.
- Multi-monitor handles and display-change recovery.
- Reduced-motion, keyboard, screen-reader, and high-contrast support.
- Tray menu and configurable shortcut.

Exit criteria:

- UI remains responsive at 60 FPS on the target machine.
- All essential actions are keyboard accessible.
- Theme and monitor changes do not require restart.

### Milestone 5: application volume and settings

- Core Audio session enumeration and conservative matching.
- Volume/mute UI with confidence-aware availability.
- Versioned settings with atomic writes and recovery from malformed data.
- Preferred/ignored players, monitor selection, theme, motion, and timeout settings.
- Start-with-Windows option using a supported Windows mechanism.

Exit criteria:

- Volume never changes an unrelated application in the test matrix.
- Settings survive upgrades and invalid configuration gracefully.

### Milestone 6: packaging and MVP release

- MSIX package and manifest capabilities.
- Local signing/development installation instructions.
- Release build, versioning, icons, uninstall verification, and upgrade test.
- Privacy document and diagnostics export.
- Performance, soak, and compatibility test pass.

Exit criteria:

- Clean install, upgrade, launch-at-login, and uninstall work.
- No secrets, user metadata, or local certificates are committed.
- MVP acceptance criteria are satisfied.

## 8. MVP acceptance criteria

### Interaction

- Top-centre handle opens the drawer through click and drag.
- Drag movement is visually continuous and completes based on distance or velocity.
- Drawer dismisses by upward drag, outside click, and Escape.
- Collapsed handle does not appear in taskbar or Alt+Tab.
- Multi-monitor placement remains correct across different scale factors.

### Media

- Compatible players appear automatically.
- Active player is sensible and can be overridden manually.
- Metadata and artwork update on track changes.
- Play/pause, previous, next, and seek work when advertised.
- Multiple sessions can be browsed without losing their individual state.
- Malformed or missing metadata is handled cleanly.

### Quality

- No continuous high-frequency process or media polling.
- Idle CPU target below 1% on the target machine.
- Memory target below 150 MB after steady-state use, subject to measured WinUI baseline.
- Drawer animation targets 60 FPS.
- No unhandled exceptions during an eight-hour session churn/soak test.
- Essential controls meet keyboard and accessibility-name requirements.

## 9. Additional feature roadmap

Additional capabilities must not delay a stable MVP. Each should be gated, separately testable, and privacy-conscious.

### 9.1 Last.fm integration

- OAuth-style account connection without storing the user's password.
- Store session credentials using Windows Credential Manager or DPAPI.
- Send now-playing updates.
- Scrobble after configurable Last.fm-compatible thresholds.
- Deduplicate repeated media-session events and track restarts.
- Pause scrobbling in private mode or for ignored players.
- Queue failed scrobbles locally and retry with bounded exponential backoff.
- Love/unlove the current track where supported.
- Show recent tracks, play count, loved state, and listener statistics.
- Optionally use Last.fm metadata to enrich missing artwork or disambiguate tracks, with visible provenance.
- Provide a reviewable local activity queue and a one-click data purge.

### 9.2 ListenBrainz and MusicBrainz

- Optional ListenBrainz now-playing and listen submission.
- MusicBrainz recording/release matching for durable identifiers.
- User confirmation for ambiguous matches.
- Metadata normalization without overwriting the player-reported display text.
- Open current artist, release, or recording in the user's browser.

### 9.3 Lyrics

- Time-synchronised and plain lyrics through user-configured legal providers.
- Local lyrics cache with clear source attribution and expiry.
- Compact line view in the main drawer and an optional expanded lyrics panel.
- Manual offset correction for synchronised lyrics.
- Translation display as a secondary line when a provider permits it.
- Never scrape or redistribute lyrics contrary to provider terms.

### 9.4 Local listening history and insights

- Explicit opt-in before retaining listening history.
- Local SQLite store with documented schema and retention controls.
- Recently played view, top artists, albums, tracks, time-of-day patterns, and listening streaks.
- Filters by source player and private-session exclusions.
- Export to JSON/CSV and delete-all controls.
- Optional history encryption at rest using DPAPI-protected keys.

### 9.5 Audio-reactive visualizer

- WASAPI loopback capture of the selected or default output device.
- FFT-based bands with smoothing and silence detection.
- Composition-rendered shapes that respond to energy without blocking the UI thread.
- Battery-saver, remote-session, and reduced-motion disable rules.
- No audio samples written to disk or transmitted.

### 9.6 Smart session handoff

- Detect when a newly playing session should take focus.
- Preserve a manually pinned player.
- Optional pause-old-player policy when a new player begins.
- Remember preferred player by time, monitor, audio endpoint, or connected device.
- Provide transparent explanations in diagnostics for why a player became active.

### 9.7 Rich desktop actions

- Configurable global shortcut and media-key behaviour.
- Mouse wheel over the handle for volume or track seeking.
- Middle-click play/pause and configurable handle actions.
- Jump to or activate the source application.
- Copy track information or a formatted now-playing status.
- Share/search shortcuts generated from metadata, without background tracking.

### 9.8 Themes and personalization

- Theme tokens rather than arbitrary executable theme code.
- Built-in Material, minimal, monochrome, and high-contrast presets.
- Configurable dimensions, corner radius, opacity, blur, animation intensity, and handle position.
- Palette lock, wallpaper palette, or artwork palette.
- Import/export of validated declarative theme files.
- Per-player accent overrides and aliases.

### 9.9 Favourites and cross-service actions

- Unified local favourite flag independent of provider.
- Last.fm love action when connected.
- Open-search actions for Spotify, YouTube Music, Bandcamp, MusicBrainz, or a configurable provider.
- Avoid pretending cross-service catalogue matches are exact; show confirmation when identity is uncertain.

### 9.10 Focus and ambient modes

- Pin a compact now-playing strip while the full drawer is closed.
- Focus timer tied to a selected playlist/player without controlling accounts directly.
- Optional gentle track-change toast.
- Quiet hours and full-screen suppression.
- Album-art ambient mode for secondary displays.

### 9.11 Extension model

Do not introduce arbitrary in-process plugins during the MVP. First stabilize internal interfaces. A later extension model should prefer:

- Declarative themes and actions.
- Out-of-process integrations over a versioned local protocol.
- Explicit permissions and user enablement.
- Timeouts, cancellation, and isolation from the UI process.
- Signed or transparently sourced extension packages.

## 10. Privacy and security

- Core playback control operates locally.
- Network integrations are off by default.
- Each service explains what data leaves the device before connection.
- Secrets live in Windows Credential Manager or under DPAPI protection, never plaintext settings.
- Logs redact tokens, account identifiers, artwork URLs containing secrets, and private media metadata by default.
- History collection is opt-in with retention and delete/export controls.
- Artwork downloads enforce HTTPS where possible, response-size limits, timeouts, MIME validation, and cancellation.
- Deep links and external URLs are allow-listed or require confirmation.
- The application does not run elevated.

## 11. Reliability and performance design

- Event-driven session observation with cancellation-aware async operations.
- Coalesce rapid metadata events and discard stale artwork loads.
- Keep decoded artwork bounded by pixel dimensions and cache budget.
- Never block the UI thread on COM, file, network, or image work.
- Treat every player command as a request that can be rejected.
- Re-enumerate cleanly after Explorer, audio service, display, or media-player restarts.
- Use structured local logs with bounded rotation.
- Offer a diagnostics export that omits media titles unless explicitly included.

## 12. Testing strategy

### Unit tests

- Session scoring and pinning.
- Timeline interpolation and reconciliation.
- Gesture state machine, thresholds, and velocity calculation.
- Capability mapping.
- Palette contrast and fallback selection.
- Scrobble qualification, deduplication, retry, and offline queue.
- Settings migration and corruption recovery.

### Integration tests

- GSMTC enumeration and event lifecycle through a controlled test publisher.
- Command success/rejection paths.
- Artwork stream cancellation and malformed inputs.
- Core Audio matching with exact, ambiguous, and absent sessions.
- Package capability and first-run behaviour.

### Manual compatibility matrix

- Spotify for Windows.
- Edge and Chrome playing YouTube/YouTube Music.
- VLC.
- Windows Media Player.
- At least one Windows-native YouTube Music client.
- Simultaneous paused and playing sessions.
- Missing artwork, unknown duration, livestream, advertisement, and podcast metadata.
- One and multiple monitors at mixed DPI and orientation.
- Taskbar on different edges and auto-hide.
- Full-screen applications, Remote Desktop, sleep/resume, and Explorer restart.
- Keyboard, mouse, touch, screen reader, reduced motion, and high contrast.

## 13. Git and release workflow

- `main` must stay buildable.
- Use short-lived branches such as `feat/gsmtc-provider`, `feat/drawer-gesture`, and `fix/session-churn`.
- Use Conventional Commit messages.
- Keep commits atomic and explain non-obvious design decisions.
- Run relevant build and tests before committing.
- Do not commit `bin/`, `obj/`, package output, local databases, logs, credentials, certificates, or user-specific IDE state.
- Record meaningful architecture choices in `docs/decisions/`.
- Tag releases using semantic versioning after the first distributable build.
- Maintain release notes from committed changes.
- Do not add a Git remote or publish artifacts without explicit authorization.

## 14. Initial execution order

1. Select the project licence.
2. Install and verify the .NET 10 SDK and WinUI workload.
3. Scaffold the solution and tests on `feat/foundation`.
4. Build the packaged GSMTC capability spike.
5. Test actual installed media applications and record the matrix.
6. Implement the platform-neutral media model and coordinator.
7. Prototype and validate the drawer interaction.
8. Combine the media model and drawer into the functional MVP.
9. Add visual polish, accessibility, multi-monitor behaviour, and Core Audio.
10. Package, soak-test, document, and release the MVP.
11. Begin opt-in integrations, starting with Last.fm only after core playback is stable.

## 15. Key risks and mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Player exposes incomplete GSMTC data | Missing controls or metadata | Capability-aware UI, aliases, fallbacks, compatibility matrix |
| Browser audio cannot be mapped to one tab | Incorrect volume target | Conservative confidence threshold; hide volume instead of guessing |
| Top handle interferes with application chrome | Frustrating desktop interaction | Narrow hit target, per-monitor disable, configurable location and reveal shortcut |
| Topmost UI conflicts with games/full-screen apps | Distracting or unavailable drawer | Full-screen suppression settings and keyboard fallback |
| Restricted media capability complicates packaging | Installation or Store friction | Prove packaged capability in Milestone 1 and support documented sideloading |
| Artwork/network input is malformed or oversized | Memory, stability, or security issue | Bounded streams, decoding limits, validation, cancellation and cache quotas |
| Visual effects consume power | Battery and thermal impact | Composition animations, idle suspension, battery saver and reduced-effects modes |
| Last.fm duplicates scrobbles | Corrupt listening history | Deterministic track identity, qualification state machine and idempotent local queue |
| Scope expands before core interaction is stable | Delayed usable product | Enforce milestone exit criteria and keep integrations post-MVP |
