# ADR 0003 - Handle and drawer as two windows

- **Status**: Accepted, 2026-09-04 (validated on device in Milestone 2; see "Validation results" below).

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

## Validation results (2026-09-04, dual monitor at 100 % and 125 %)

- One handle per monitor at the top-centre of each work area; clicking a handle never changed the foreground window; handles have `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST` and are absent from Alt+Tab.
- The drawer opened at the correct scaled position on the 125 % monitor on its first frame.
- Cross-monitor toggle closes on the first monitor and reopens on the second.

## Amendments learned on device

- WinUI windows are opaque. Translating a full-height drawer window's content revealed the window's own black background, so the reveal **resizes the window from the top edge** (height = progress x design height) and translates the content so its bottom edge stays on the window's bottom edge. The settle is a `SpringMotion` (Core) stepped by an 8 ms UI-thread timer because the HWND size cannot be animated by the compositor. Milestone 4 may move to a composition-only reveal once a transparent/acrylic backdrop exists.
- Never edit `WS_*` styles of a WinUI window by hand (it blacks out the swap chain); only extended styles are touched, the presenter removes the frame.
- `Window.Activate` alone does not take foreground for a window first shown non-activating; `NativeInterop.ForceForeground` (SetForegroundWindow with the AttachThreadInput fallback) is used after the open settles.
