# Game Development Guide

Patterns used by the example games — steal freely.

## The loop

```mako
using Mako2D;
using Inputs;
using Audio;

main() {
    Mako2D.init(800, 600, "Game");
    Mako2D.fps(60);

    while Mako2D.running() {
        dt = Mako2D.delta();
        # 1. read input   2. update world   3. draw
        Mako2D.begin();
        Mako2D.clear(Mako2D.BLACK);
        Mako2D.end();
    }
    Mako2D.close();
}
```

## Delta time — never move per frame

```mako
x = x + 4;                  # BAD  — speed changes with FPS
x = x + 240 * dt;           # GOOD — 240 px/sec at any frame rate
```

Normalize diagonals so they aren't √2 faster:

```mako
mlen = sqrt(mvx * mvx + mvz * mvz);
if mlen > 0 { px = px + mvx / mlen * SPEED * dt; }
```

## Collision

```mako
if rects_overlap(px, py, pw, ph,  ex, ey, ew, eh) { hit(); }
if circles_overlap(x1, y1, r1,  x2, y2, r2)       { hit(); }
if dist(px, py, gem_x, gem_y) < pickup_radius      { collect(); }
if point_in_rect(Inputs.mouse_x(), Inputs.mouse_y(), bx, by, bw, bh)
   and Inputs.mouse_pressed("left")                { clicked(); }
```

Wall sliding — test each axis separately:

```mako
if grid[cell_z(pz)][cell_x(nx)] == 0 { px = nx; }
if grid[cell_z(nz)][cell_x(px)] == 0 { pz = nz; }
```

## Grid levels from ASCII maps

```mako
map = [
    "#########",
    "#...#...#",
    "#.#...#.#",
    "#########"
];
# parse to a number grid once:
grid = [];
for row_str in map {
    row = [];
    for i in range(len(row_str)) {
        cell = 0;
        if row_str[i] == "#" { cell = 1; }
        push(row, cell);
    }
    push(grid, row);
}
```

## Monster AI (the monster_maze.mko pattern)

State machine + A\* + senses:

```mako
# Senses
sees  = can_see(grid, mx, mz, facing, px, pz);   # vision cone (below)
hears = moving and dist(px, pz, mx, mz) < hear_radius;

# States: patrol -> alert (heard) -> chase (seen) -> search (lost)
if m_state == "chase" {
    if sees {
        last_seen = [pcx, pcz];
        repath_timer = repath_timer + dt;
        if repath_timer > 0.4 {                   # don't repath every frame
            repath_timer = 0;
            m_path = find_path(grid, mcx, mcz, pcx, pcz);
        }
    } else {
        lost_t = lost_t + dt;
        if lost_t > 1.0 {
            m_state = "search";                   # go to last_seen, scan, give up
            m_path = find_path(grid, mcx, mcz, last_seen[0], last_seen[1]);
        }
    }
}

# Follow the path
if len(m_path) > 0 {
    step = m_path[0];
    d = dist(mx, mz, wx(step[0]), wz(step[1]));
    if d < 0.1 { m_path = slice(m_path, 1, len(m_path)); }
    else {
        mx = mx + (wx(step[0]) - mx) / d * speed * dt;
        mz = mz + (wz(step[1]) - mz) / d * speed * dt;
    }
}
```

Vision cone — range + angle + wall check:

```mako
fn can_see(grid, mx, mz, facing, px, pz) {
    if dist(mx, mz, px, pz) > VIEW_RANGE { return false; }
    diff = atan2(pz - mz, px - mx) - facing;
    while diff > pi()     { diff = diff - 2 * pi(); }
    while diff < 0 - pi() { diff = diff + 2 * pi(); }
    if abs(diff) > FOV / 2 { return false; }
    return line_of_sight(grid, cell_x(mx), cell_z(mz), cell_x(px), cell_z(pz));
}
```

Face movement direction smoothly:

```mako
want = atan2(tz - mz, tx - mx);
facing = facing + wrap_angle(want - facing) * min(1, 8 * dt);
```

## Sound design without asset files

```mako
snd_hit  = Audio.tone("square", 440, 0.06);                  # blip
snd_die  = Audio.melody("saw", ["G3:0.5", "E3:0.5", "C3"], 200);  # jingle
snd_step = Audio.tone("noise", 200, 0.04);                   # footstep

Audio.pitch(snd_eat, 1 + score * 0.03);   # rising pitch with combo/score
Audio.play_at(snd_hit, x, y);             # pans to where it happened
Audio.play_3d(growl, mx, 1, mz, px, 1, pz);  # louder as it approaches
```

## Timers, animation pulses

```mako
t = t + dt;                                # global clock
bob   = sin(t * 3) * 0.25;                 # floating pickup
pulse = 1 + sin(t * 8) * 0.08;             # menacing heartbeat scale

step_t = step_t + dt;                      # fire on an interval
if step_t > 0.22 { step_t = 0; Audio.play(snd_step); }
```

Invulnerability flash after a hit:

```mako
if hurt_cool > 0 and floor(hurt_cool * 8) % 2 == 0 { skip_drawing_player; }
```

## Game states

Keep one `state` string — `"play"`, `"win"`, `"lose"` — and gate the update
logic on it; draw overlays for the end states; reset everything on SPACE.
See `pong.mko`, `snake.mko`, `gem_hunter.mko`, `monster_maze.mko`.
