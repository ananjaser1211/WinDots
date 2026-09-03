# WinDots

WinDots is a native Windows media drawer inspired by the fluid interaction and visual character of Caelestia's media dashboard.

Pull down from a small handle at the top-center of a monitor to reveal the music currently playing anywhere on Windows. WinDots discovers compatible players automatically and presents their artwork, metadata, timeline, controls, and audio-session volume in one polished surface.

WinDots is not a port of Caelestia and does not require Linux, WSL, Hyprland, Quickshell, a virtual machine, or a companion service. It is an independent Windows application built with C#, WinUI 3, the Windows App SDK, Windows media-session APIs, and Core Audio.

## Product vision

WinDots should feel like part of the desktop rather than another application window:

- A subtle grab handle rests at the top-center of each enabled monitor.
- Pulling the handle down reveals the player and tracks the pointer continuously.
- Releasing with sufficient distance or velocity completes a spring animation.
- Swiping upward, pressing Escape, or clicking outside dismisses the drawer.
- Artwork drives the colour palette and animated background treatment.
- Spotify, YouTube Music, browsers, VLC, and other compatible players appear without application-specific integrations.
- Multiple active players can be switched from the drawer.
- The collapsed handle does not activate the app or take over a large click region.

## MVP

The first production-ready version will include:

- Native Windows 11 top-center pull-down drawer.
- Primary and multi-monitor support with per-monitor DPI handling.
- Windows Global System Media Transport Controls session discovery.
- Active-player selection and a manual player switcher.
- Album artwork, title, artist, album, source application, and playback state.
- Play/pause, previous, next, and seek controls when supported by the player.
- Smooth progress interpolation between media-session updates.
- Per-application volume and mute where a media session can be safely matched to a Core Audio session.
- Desktop Acrylic with solid-colour fallbacks.
- Artwork-derived dynamic colour palettes.
- Keyboard shortcut, tray menu, start-with-Windows setting, and reduced-motion mode.
- Resilient handling of stopped, replaced, incomplete, and disappearing sessions.
- Local settings and diagnostics without recording listening history by default.
- Packaged installation and uninstall support.

## Later capabilities

The roadmap includes Last.fm now-playing and scrobbling, lyrics, a local listening history, ListenBrainz, favourites, audio-reactive visuals, richer player rules, themes, optional extensions, and accessibility improvements. See [IMPLEMENTATION.md](./IMPLEMENTATION.md) for scope, architecture, milestones, acceptance criteria, and the longer-term feature catalogue.

## Media compatibility

WinDots uses Windows' system media-session interface rather than scraping application windows. A player is supported when it publishes a Windows System Media Transport Controls session.

Expected targets include:

- Spotify for Windows.
- YouTube Music in Edge or Chrome.
- Windows-native YouTube Music desktop clients that publish a media session.
- Microsoft Edge and Chromium-based browsers.
- VLC, Media Player, and other compatible desktop applications.

Individual capabilities vary by player. WinDots will only enable commands that a session reports as supported. Volume is provided through Windows Core Audio and may be shared across several browser tabs or processes.

## Technology

- C# and .NET 10.
- WinUI 3 and the latest stable Windows App SDK selected when the project is scaffolded.
- `Windows.Media.Control` for global media sessions.
- Windows Core Audio COM interfaces for application volume.
- Windows Composition and DWM APIs for motion, backdrop, window shape, and placement.
- MSIX packaging for a declared `globalMediaControl` capability and reliable installation.

## Repository status

The repository currently contains the approved product and implementation plan. Application scaffolding begins with the capability-spike milestone described in `IMPLEMENTATION.md`.

## Development principles

- Windows-native first; no WSL runtime dependency.
- Event-driven media updates rather than continuous polling.
- Small, non-invasive screen-edge activation area.
- Capability-aware controls and graceful degradation.
- Local-first privacy and explicit consent for network integrations.
- Accessible interaction and reduced-motion support from the beginning.
- Atomic, tested Git commits with no secrets or generated artifacts.

## License

The project license has not yet been selected. Caelestia is a visual and interaction reference only. Do not copy Caelestia source code, assets, icons, or other GPL-covered implementation into WinDots unless the project explicitly adopts a compatible licence and the change is documented.
