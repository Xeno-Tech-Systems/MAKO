# Rooms — networked party play (design)

Goal: let a group of friends join the same game session over the
internet — one player creates a room, gets a short join code, shares it,
others join with that code, and everyone's player state is relayed to
everyone else in the room. This is genuinely new: `Net` (docs/net.md)
only does one-shot HTTP GET/POST, no persistent connections, no concept
of a room or session.

## Why a relay server, not pure peer-to-peer

Most home internet connections sit behind NAT, so two players' game
clients generally can't open a direct connection to each other without
extra machinery (STUN/TURN/hole-punching) that's a project of its own.
The standard, working answer for "invite a friend" style small multiplayer
is a small relay server both clients connect *out* to (always allowed,
NAT is not a problem for outbound connections) — the server just forwards
bytes between everyone in the same room. This is the same shape real
indie multiplayer games use for exactly this scenario.

## Two halves

1. **The relay server** — a small, separate program (`src/Mako.Rooms/`,
   ASP.NET Core + WebSockets, same "own small project" pattern
   `Mako.Web` already established for the browser build). Not part of
   the `mko` CLI itself — you run it once, anywhere reachable (your own
   machine for local testing, a small VPS for real friends-over-the-
   internet play), and any MAKO game just points at its address.
2. **The MAKO-side `Room` package** — a new native package, `using
   Room;`, giving scripts a small, typeable API to create/join a room
   and send/receive player state each frame.

## Wire protocol (server ↔ client)

Plain JSON over a WebSocket connection — MAKO already has a real,
working `json_encode`/`json_decode` built in, so no new serialization
format to invent. One message type per line-of-intent:

**Client → server:**
```json
{"type": "create"}
{"type": "join", "code": "ABCD"}
{"type": "state", "data": {"x": 3.5, "y": 0, "jumping": false}}
{"type": "leave"}
```

**Server → client:**
```json
{"type": "created", "code": "ABCD", "player_id": 1}
{"type": "joined", "code": "ABCD", "player_id": 2}
{"type": "error", "message": "room not found"}
{"type": "player_state", "player_id": 2, "data": {"x": 3.5, "y": 0, "jumping": false}}
{"type": "player_left", "player_id": 2}
```

- `code` is a short, human-typeable string (4 uppercase letters, e.g.
  `ABCD`) — easy to read aloud or type into a chat, the same design
  instinct behind every other MAKO-adjacent naming choice this project
  has made ("if it's hard to type, it's hard to understand").
- `player_id` is assigned by the server in join order (room creator is
  always 1) — this reuses the exact same "player number, not a raw
  connection id" mental model `Players` (docs/players.md) already
  established for local multiplayer, so the two systems feel like one
  consistent idea instead of two unrelated ones.
- `data` in a `state`/`player_state` message is an arbitrary MAKO
  dict — the relay server never inspects it, just forwards it verbatim
  to everyone else in the room. What fields a game puts in there (`x`,
  `y`, an action name, anything) is entirely up to the game script, not
  something the protocol prescribes.

## The `Room` package (client-side MAKO API)

```mako
using Room;

main() {
    Room.connect("ws://localhost:5000");   # or a real server address
    code = Room.create();                  # returns "ABCD"-style code
    print "Room code: " + code;

    while Room.connected() {
        Room.send({"x": my_x, "y": my_y});
        for p in Room.players() {
            other = Room.state(p);
            if other != none {
                # draw other[x], other[y] ...
            }
        }
    }
}
```

Joining an existing room is symmetric:
```mako
Room.connect("ws://localhost:5000");
Room.join("ABCD");
```

| Function | What it does |
|---|---|
| `connect(url)` | Opens the WebSocket connection to a relay server |
| `create()` | Creates a room, returns its join code |
| `join(code)` | Joins an existing room by code |
| `connected()` | Still connected to the server/room |
| `send(data)` | Sends this player's state dict to everyone else in the room |
| `players()` | List of other players' IDs currently in the room |
| `state(player_id)` | That player's most recently received state dict, or `none` |
| `leave()` | Leaves the room, stays connected to the server |

Deliberately mirrors `Players`' own naming register (short verbs, no
jargon) rather than reusing generic networking terms like "socket"/
"peer"/"session" — a script author who already knows `Players.x(1)`
should find `Room.send(...)`/`Room.players()` equally unsurprising.

## What's deliberately not in scope for v1

- **Reconnection after a dropped connection** — a real, valuable
  feature, but adds real state-reconciliation complexity (what happens
  to a player's room slot while they're reconnecting?) deliberately
  deferred until the basic connect/create/join/relay loop is proven
  working.
- **Server-authoritative anything** (anti-cheat, validating that a
  player's claimed position is physically possible) — this is a relay,
  not a game server; it forwards whatever a client sends, trusting it,
  same as how most small/indie multiplayer games start before adding
  real server-side validation later if it's ever needed.
- **Room persistence/history** — a room exists only as long as at least
  one player is connected to it; the server holds no database, nothing
  survives a server restart. Simplicity over durability for a first
  version.
- **More than one relay server / matchmaking across servers** — v1 is
  "point your game at one relay server's address," not a
  server-discovery or load-balancing system.
