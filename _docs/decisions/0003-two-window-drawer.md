# ADR 0003 - Handle and drawer as two windows

- **Status**: Proposed, 2026-09-04. Validate during Milestone 2 and update to Accepted or Superseded.

## Context

The collapsed handle must be non-activating, topmost, tiny, and present on every monitor, while the expanded drawer must accept focus, host WinUI content, use Acrylic, and animate its reveal.

## Decision (proposed)

- One lightweight **handle window per monitor**: Win32 popup with `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_LAYERED`, drawn with a composition visual, capturing the pointer during drag.
- One shared **drawer window**: WinUI `Window` moved to the active monitor on open, shown at `progress = 0` (fully translated above the screen edge) and translated by composition as the gesture proceeds.
- The handle forwards pointer samples to `IDrawerController`; the drawer view binds to `Progress`.

## Alternatives considered

A single window tall enough for the drawer, always present but mostly transparent. Rejected provisionally because a large transparent topmost window intercepts clicks on other apps' title bars and Acrylic cannot be partially applied.

## Validation criteria (M2)

- Drag from handle to drawer is visually continuous with no frame where neither window paints.
- The collapsed handle never appears in Alt+Tab or the taskbar and never activates.
- Opening on a second monitor with a different DPI renders at the correct scale on the first frame.

If any criterion fails, revisit with a single-window design using `WS_EX_TRANSPARENT` regions.
