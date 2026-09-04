# 02 - Product specification

Phases are cumulative. Phase A is the MVP and must ship stable before Phase B starts.

## Phase A - Media drawer (MVP)

### A1. Handle

- **Story**: as a user I see a thin, unobtrusive bar at the top-centre of my monitor and can pull it down.
- Logical size 160 x 6 px, hit target 200 x 12 px, per-monitor enable.
- Widens and brightens on hover; never activates or steals focus while collapsed.
- Hidden while a full-screen exclusive app is in the foreground (setting).
- States: `Idle`, `Hover`, `Pressed`, `Hidden`.
- Accessibility: exposed as a button named "Open WinDots media drawer" for screen readers; the global shortcut is the keyboard path.

### A2. Drawer shell

- Pulled down from the handle, tracks the pointer, springs open/closed (see `03-ux-interaction-spec.md`).
- Contains the tab strip and one content page. Phase A shows only the Media tab; the strip renders one item.
- Size 720 x 300 logical px at 100 % scale, clamped to 90 % of the work-area width.
- Dismiss: upward drag, Escape, click outside, inactivity timeout (setting, default off), after-command auto-dismiss (setting, default off).
- Backdrop: Desktop Acrylic; opaque fallback when transparency is off or on battery saver.

### A3. Artwork blob and progress ring

- Artwork clipped to a scalloped blob; deform amount from `appearance.blobDeform`.
- Dotted ring: 72 dots; dot *i* is accent-coloured when `i / 72 <= position / duration`.
- Placeholder glyph when artwork is missing; cross-fade 250 ms on change.
- Ring hidden when duration is unknown (livestream) and replaced by a slow pulse unless reduced motion is on.

### A4. Metadata

- Title, artist, album. Missing values render `Unknown title` / `Unknown artist` / `Unknown album` exactly as the reference.
- Source line is the player alias (from `media.playerAliases`) or the app display name.
- Text truncates with an ellipsis; full text in the tooltip and accessible name.

### A5. Seek

- Elapsed / duration labels in `m:ss` (`h:mm:ss` above one hour).
- Position interpolates locally between timeline events at `media.timelineTickMs`.
- Drag or click seeks when the session advertises `IsPlaybackPositionEnabled`; otherwise the slider is read-only and announces "Seeking not supported by this player".
- Optimistic UI: show the target position immediately, reconcile on the next timeline event, revert with a status toast if rejected.

### A6. Transport

- Shuffle, previous, play/pause, next, repeat. Each enabled only when the capability is advertised.
- Play/pause is the primary pill and is the default focused control when the drawer opens.
- Repeat cycles None, List, Track when supported; shows a dot under the icon when active.
- Rejected commands show a non-blocking status line for 2 s; the UI never pretends a command succeeded.

### A7. Volume

- Shown only when the Core Audio match confidence is `High` (see `05-architecture.md`). Otherwise the row collapses and the overflow menu explains why.
- Mute toggle, slider 0-100, percentage label. Scroll wheel over the row adjusts by 2 %.
- Never falls back to master volume.

### A8. Player chooser

- Pill showing the active player's alias and icon; the chevron opens a list of every discovered session with its playback state.
- Picking a player pins it until it disappears or the user selects "Automatic".
- The empty state still shows the chooser with the Windows current session's source, matching `Static.png`.

### A9. Lyrics panel (shell only in Phase A)

- Header, overflow menu, body area. Phase A always renders the `No lyrics found` state.
- Overflow menu items in Phase A: "Copy track info", "Open in player", "Why is volume hidden?" (when applicable), "Settings".

### A10. Tray, shortcut, startup

- Tray icon with Open / Pause all / Settings / Quit.
- Configurable global shortcut (default `Win+Shift+M`) toggles the drawer on the monitor with the pointer.
- Start with Windows via the packaged `StartupTask`.

### A11. Settings window

- Simple WinUI settings page: monitors, gesture, appearance, media rules, privacy, diagnostics export. Backed by `06-settings-schema.md`.

### A12. Empty and error states

- No session: `Static.png` layout.
- Session vanished mid-use: fade to the empty state, keep the drawer open, no exception.
- Media access denied: message with a link to Windows privacy settings.

## Phase B - Dashboard tab

- Tab strip gains **Dashboard**.
- Clock (stacked hh / mm / AM-PM), month calendar, mini media card (reuses A3-A6 components at small size), resource rings (CPU, memory, primary disk via Windows performance counters), user card (account picture, uptime).
- Weather card appears only after the user enables weather and enters a location (network consent dialog). Provider chosen by ADR at the time.
- Optional user-supplied mascot image path; nothing bundled.

## Phase C - Remaining tabs and integrations

- **Performance tab**: CPU, GPU, memory, storage, network, battery graphs.
- **Weather tab**: current conditions and forecast, same provider as B.
- **Lyrics**: provider abstraction, synced lines, offset control, cache with attribution.
- **Visualiser**: WASAPI loopback, FFT bands, composition-rendered, `visualiser.bars`.
- Last.fm, ListenBrainz, history, favourites, themes, focus modes and more as catalogued in `10-enhancement-plan.md`.

## Non-goals (all phases)

See the `AGENTS.md` hard constraints. In short: no Linux runtime, no UI scraping, no elevated install, no telemetry, no bundled third-party art.
