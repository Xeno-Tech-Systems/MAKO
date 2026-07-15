# Controllers — named gamepad input

Use `Players` for the easiest local multiplayer. Use `Controllers` when a game
needs both sticks, controller names, or individual named buttons.

```mako
using Controllers;

if Controllers.connected(1) {
    print Controllers.name(1);
    look_x = Controllers.right_x(1);
    if Controllers.pressed(1, "a") { ... }
}
```

Player numbers are 1–4. Stick functions are `left_x`, `left_y`, `right_x`, and
`right_y`, with a built-in deadzone. `down(player, button)` and
`pressed(player, button)` accept `a`, `b`, `x`, `y`, `jump`, `action`, `back`,
`start`, `pause`, `select`, directions, `l1`, and `r1`.
