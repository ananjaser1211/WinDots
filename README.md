# WinDots

A pull-down media drawer for Windows 11. A thin handle sits at the top-centre of the screen; drag it down to see what is playing, control it, and switch between players. Inspired by the media dashboard in Caelestia.

## What it does

- Discovers any player that publishes a Windows media session (browsers, Spotify, Media Player, and others) with no per-app integration.
- Shows artwork, title, artist, album, source app, and a live progress timeline.
- Play/pause, previous, next, seek, shuffle, and repeat when the player supports them.
- Per-app volume where the audio session can be matched safely.
- Opens by drag, click, or a global shortcut; closes with an upward swipe, Escape, or a click outside.
- Multi-monitor and per-monitor DPI aware; acrylic backdrop with artwork-derived colours.

Built with C#, .NET 10, WinUI 3, and the Windows App SDK. Packaged as MSIX.

## Status

Early development. The solution, media-session adapter, drawer gesture engine, and monitor service exist with tests; the drawer UI is in progress. See [`_docs/08-roadmap.md`](./_docs/08-roadmap.md) for milestones and [`_docs/09-dev-environment.md`](./_docs/09-dev-environment.md) to build and run.

## Documentation

Specifications, architecture, settings schema, test matrix, and decision records live in [`_docs/`](./_docs/README.md). `IMPLEMENTATION.md` is the top-level plan and `AGENTS.md` holds contributor rules.

## License

GPL-3.0-or-later. See [LICENSE](./LICENSE).
