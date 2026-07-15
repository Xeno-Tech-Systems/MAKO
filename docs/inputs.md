# Inputs — unified input

Works alongside any window (Mako2D, Mako3D, MakoRay):

```mako
using Mako2D;
using Inputs;

# in the game loop:
if Inputs.key_down("W")          { ... }   # held right now
if Inputs.key_pressed("SPACE")   { ... }   # went down this frame
if Inputs.key_released("SPACE")  { ... }   # went up this frame
if Inputs.mouse_down("left")     { ... }
```

## Keyboard

| Function | Description |
|---|---|
| `key_down(key)` | Held |
| `key_pressed(key)` | Pressed this frame (one-shot) |
| `key_released(key)` | Released this frame |
| `key_up(key)` | Not held |
| `last_key()` | Name of the most recent key event, `""` if none |
| `any_key()` | Any key pressed this frame? |

**Key names:** single characters (`"A"`, `"7"`) plus:
`SPACE ENTER ESCAPE TAB BACKSPACE DELETE INSERT HOME END PAGEUP PAGEDOWN
UP DOWN LEFT RIGHT SHIFT CTRL ALT` (also `LSHIFT/RSHIFT` etc.) and
`F1`–`F12`. Case-insensitive.

## Mouse

| Function | Description |
|---|---|
| `mouse_x()` / `mouse_y()` | Cursor position |
| `mouse_delta_x()` / `mouse_delta_y()` | Movement since last frame |
| `scroll()` | Wheel movement this frame |
| `mouse_down(btn)` / `mouse_pressed(btn)` / `mouse_released(btn)` / `mouse_up(btn)` | Buttons: `"left"`, `"right"`, `"middle"`, `"back"`, `"forward"` |

## Cursor

| Function | Description |
|---|---|
| `hide_cursor()` / `lock_cursor()` | Hide + capture (FPS-style mouse-look) |
| `show_cursor()` / `unlock_cursor()` | Restore |

## Gamepad

For local multiplayer, prefer the easier [`Players`](players.md) package. It
uses player numbers, named actions, automatic keyboard controls, and gamepads
without exposing raylib button indices.

| Function | Description |
|---|---|
| `gamepad_ready(pad)` | Controller connected? |
| `gamepad_btn(pad, button)` | Button held (raylib button indices) |
| `gamepad_axis(pad, axis)` | Stick axis −1..1 |

## Notes

- Raylib's default "ESC closes the window" is disabled — ESC is a normal
  key. Handle quitting yourself: `if Inputs.key_pressed("ESCAPE") { ... }`.
- Run `mko input_test.mko` to see every input light up live.
