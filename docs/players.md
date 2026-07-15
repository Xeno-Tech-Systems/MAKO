# Players — easy local multiplayer

`Players` gives up to four local players the same tiny API whether they use a
keyboard or gamepad. Keyboard and controller input work side by side: plugging
in an idle controller never disables WASD or the arrow keys.

```mako
using Players;

x1 = Players.x(1);                  # WASD or gamepad 1
x2 = Players.x(2);                  # arrows or gamepad 2
if Players.pressed(1, "jump") { ... }
```

| Function | What it does |
|---|---|
| `connected(player)` | Player is ready; keyboard players 1–2 always are |
| `x(player)` | Horizontal movement from −1 to 1 |
| `y(player)` | Vertical movement from −1 to 1 |
| `down(player, action)` | Named action is held |
| `pressed(player, action)` | Named action started this frame |

Actions are `"jump"`, `"action"`, `"back"`, and `"pause"`. Player 1 uses
WASD, Space, E, and Escape. Player 2 uses arrows, Enter, Right Control, and
Backspace. Connected gamepads automatically take priority and include a small
stick deadzone. Player numbers run from 1 through 4.
