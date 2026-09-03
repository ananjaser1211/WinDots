# 03 - Interaction specification

## Geometry

| Element | Logical px at 100 % | Notes |
|---|---|---|
| Handle visual | 160 x 6 | Rounded 3 px, centred on the monitor's top edge |
| Handle hit target | 200 x 12 | Includes 3 px above the visual so edge-slams register |
| Handle hover visual | 200 x 8 | 120 ms ease-out |
| Drawer | 720 x 300 | Clamped to 90 % of work-area width, 60 % of height |
| Drawer top offset | 0 | Flush with the top edge; if the taskbar is on top, sits below it |

All values scale with the monitor's DPI; text additionally scales with `appearance.fontScale`.

## Drawer state machine (`IDrawerController`, lives in `WinDots.Core`)

```
Closed --pointerDown(handle)--> Dragging --release--> Settling(Open | Closed) --animationDone--> Open | Closed
Closed --toggle()------------------------------------> Settling(Open)
Open   --pointerDown(drawer top 24 px)--> Dragging
Open   --escape | clickOutside | timeout | toggle()--> Settling(Closed)
Open   --drag up past threshold----------------------> Settling(Closed)
```

- `progress` in [0, 1] is the only output; the view maps it to a translateY of `(progress - 1) * drawerHeight`.
- While `Dragging`, `progress = clamp(dy / drawerHeight)` with rubber-band resistance above 1: `1 + 0.15 * tanh((dy - h) / h)`.
- Horizontal movement never cancels a gesture; it is ignored.
- Release decision: open if `progress >= drawer.openThreshold` (default 0.35) **or** downward velocity >= `drawer.velocityThresholdPxPerS` (default 600). Close if upward velocity >= the same. Otherwise snap to the nearer state.
- `dragThresholdPx` (default 50) is the minimum travel before a press becomes a drag; below it, release on the handle is a **click** and toggles.
- Velocity is a 60 ms windowed average of pointer samples, computed in Core from timestamps supplied by the view.
- Mouse, pen, and touch feed the same machine via `PointerPoint` timestamps and positions.

## Settling animation

- Spring: stiffness 320, damping 28, mass 1; settled when |v| < 0.5 px/s and |x - target| < 0.5 px. Implemented as a Composition `SpringScalarNaturalMotionAnimation` on the drawer visual; Core only exposes the target.
- Reduced motion: 150 ms linear fade plus 8 px slide instead of the spring.

## Dismissal paths

| Path | Condition |
|---|---|
| Upward drag | From the drawer's top 24 px band or the handle |
| Escape | Drawer window has focus |
| Click outside | Pointer-down outside the drawer window; implemented by a WM_ACTIVATEAPP / foreground-change watcher, not a global hook |
| Inactivity | `drawer.autoHideMs` > 0 and no pointer/keyboard activity |
| After command | `drawer.hideAfterCommand` true and a transport command succeeded |
| Full-screen app | Foreground window covers the monitor and `drawer.hideInFullscreen` true |

## Focus and activation

- Handle window: `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST`; never takes focus.
- Drawer window: activates on open so keyboard works; returns focus to the previous foreground window on close.
- Initial focus lands on play/pause.

## Keyboard map (drawer open)

| Key | Action |
|---|---|
| Space / Enter on focused control | Activate |
| Escape | Close |
| Left / Right | Previous / next track when a transport button has focus; seek +/-5 s when the seek slider has focus |
| Shift+Left / Right | Seek +/-30 s |
| Up / Down | Volume +/-2 % when the volume slider has focus |
| M | Mute toggle |
| P | Open player chooser |
| Ctrl+Tab / Ctrl+Shift+Tab | Next / previous tab (Phase B+) |
| Tab order | tab strip, title (read-only), seek, shuffle, previous, play, next, repeat, volume, lyrics menu, player chooser; artwork is not focusable |

Global: `drawer.toggleShortcut` (default `Win+Shift+M`) toggles the drawer on the monitor containing the pointer. Media keys are left to Windows.

## Multi-monitor

- One handle per enabled monitor. Only one drawer is open at a time; opening on another monitor closes the first.
- On `WM_DISPLAYCHANGE`, `WM_DPICHANGED`, work-area change, or taskbar move, all handles reposition within 100 ms; an open drawer closes first.

## Accessibility

- Every control has an `AutomationProperties.Name`; state changes (play to pause, rejected command) raise live-region announcements.
- High contrast: tokens switch to system colours, the blob outline becomes a solid stroke, ring dots become a solid arc.
- Reduced motion: no spring, no blob drift, no ring rotation, artwork cross-fade becomes an instant swap.
- Text scaling: layout reflows to two rows (artwork + metadata above, lyrics below) above 150 % text scale.
