### Camera Feature Compatibility Matrix

- Features:
  - [x] Drag Pan (pixel-perfect)
  - [x] Zoom (cursor-anchored, smoothed; WheelUp=in, WheelDown=out)
  - [x] Inertia (lookahead seconds, linear decel; zoom cancels inertia)
  - [x] ESC Cancel Drag
  - [ ] Bounds / UI Blocking (future)

| Interaction                          | Drag Pan | Zoom | Inertia | ESC Cancel | Notes |
|--------------------------------------|----------|------|---------|------------|-------|
| Drag Pan alone                       | [x]      | n/a  | n/a     | n/a        | Pixel-perfect anchor under cursor |
| Zoom while not dragging              | n/a      | [x]  | n/a     | n/a        | Cursor-anchored smoothing |
| Zoom during drag                     | [x]      | [x]  | n/a     | n/a        | Fixed zoom anchor captured at first wheel during drag |
| Release -> Inertia (no zoom)         | [x]      | n/a  | [x]     | n/a        | Linear decel over lookahead seconds |
| Zoom during inertia (override inert) | n/a      | [x]  | [x]     | n/a        | Zoom cancels inertia immediately |
| ESC during drag                      | [x]      | n/a  | n/a     | [x]        | Cancels drag immediately |

Legend: [x] implemented, [ ] pending, n/a not applicable.
