# MAKO

**A simple, sharp programming language — with a game engine built in.**

MAKO blends the readability of Python, the simplicity of Lua, and the structure of C-style languages. Write a script, run it with `mko`, and you have windows, 3D graphics, synthesized sound, and monster AI — no project files, no build step, no asset pipeline required.

> **Status:** v0.03 — dicts, lambdas, try/catch, formatter, REPL, packages, and five native packages: **MakoUI**, **Mako2D**, **Mako3D**, **Inputs**, **Audio**.

```mako
using Mako2D;
using Inputs;
using Audio;

main() {
    Mako2D.init(800, 600, "My Game");
    Mako2D.fps(60);
    beep = Audio.tone("square", 440, 0.1);   # synthesized — no sound files
    x = 400;

    while Mako2D.running() {
        if Inputs.key_down("RIGHT") { x = x + 300 * Mako2D.delta(); }
        if Inputs.key_pressed("SPACE") { Audio.play(beep); }

        Mako2D.begin();
        Mako2D.clear(Mako2D.BLACK);
        Mako2D.circle(x, 300, 24, Mako2D.SKYBLUE);
        Mako2D.end();
    }
}
```

---

## Install

Needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and git.

```bash
# Arch / Abora
sudo pacman -S dotnet-sdk

# Debian / Ubuntu
sudo apt install dotnet-sdk-8.0
```

```bash
git clone https://github.com/AnimatedGTVR/MAKO
cd MAKO
./build.sh install
```

That installs `mko` to `~/.local/bin` (plus native libs and all examples). Then, from anywhere:

```bash
mko hello.mko          # examples resolve from anywhere
mko pong.mko           # play Pong
mko repl               # interactive REPL
```

Scripts can also be made executable directly:

```mako
#!/usr/bin/env mko
main() { print "hello!"; }
```

## Try the games

All examples install globally — run these from any directory:

| Command | What it is |
|---|---|
| `mko pong.mko` | Pong vs an AI paddle — synth sound, positional audio |
| `mko snake.mko` | Snake — speeds up as you grow, pitch-shifting eat sound |
| `mko gem_hunter.mko` | 3D arena collector — camera follow, patrol enemies, navigate by ear |
| `mko monster_maze.mko` | **Stealth game** — monster AI with a vision cone, hearing, A* pathfinding |
| `mko music_maker.mko` | Playable synth piano — 5 waveforms, bake melodies from note lists |
| `mko settings.mko` | FPS overlay + graph, audio sliders, FPS cap selector |
| `mko ui_demo.mko` | Dear ImGui desktop UI — menus, tables, modals, cherry-blossom theme |
| `mko sound_3d.mko` | 3D positional audio — beacons that pan and fade as you fly |
| `mko input_test.mko` | Visual input tester — every key/button lights up live |

## The language in 60 seconds

```mako
script "Tour";                       # optional script header

const MAX = 100;                     # top-level constants

fn greet(name) {                     # functions
    return "Hello, {name}!";         # string interpolation
}

main() {
    # Types: numbers, strings, booleans, none, lists, dicts
    nums = [3, 1, 2];
    user = {"name": "Robin", "score": 42};

    # Lambdas + higher-order builtins
    doubled = map(nums, fn(x) => x * 2);
    big = filter(nums, fn(x) => x > 1);

    # Control flow
    for n in nums { print n; }
    while user["score"] < MAX { user["score"] = user["score"] + 10; }

    # Error handling
    try {
        assert(false, "boom");
    } catch err {
        print "oops: {err}";
    }

    # Files, shell, time
    write("save.txt", "data");
    run "echo from the shell";
    print time();
}
```

Errors point at the problem and suggest fixes:

```
  print greeet("World");
        ^^^^^^
mko: error (line 4): function 'greeet' wasn't found
  hint: did you mean 'greet'?
```

## CLI

| Command | Does |
|---|---|
| `mko <file.mko>` / `mko run <file>` | Run a script |
| `mko repl` | Interactive REPL |
| `mko fmt <file>` [`--check`] | Format source (preserves comments) |
| `mko get <pkg> [github:User/Repo]` | Install a package |
| `mko list` / `mko cache clear` | Manage installed packages |

Packages come from GitHub with one line:

```mako
using mylib from "github:User/Repo";
```

## Native packages

| Package | What it gives you |
|---|---|
| `using Mako2D;` | 2D games: sprites, spritesheets, Camera2D, shapes, text |
| `using Mako3D;` | 3D games: Camera3D (fly/orbit controls built in), cubes/spheres/models, grid |
| `using Inputs;` | Unified input: keyboard, mouse, cursor lock, gamepad |
| `using Audio;` | Sound files + **synthesizer** (5 waveforms, note names, melody baking), music streaming, **2D/3D positional sound**, 8-voice polyphony |
| `using MakoUI;` | Desktop UIs via Dear ImGui: windows, menus, tables, modals, themes |

Game-dev builtins are always available — no import needed:

```mako
clamp(v, 0, 10)   lerp(a, b, t)   dist(x1,y1, x2,y2)   sin/cos/atan2/pi
rects_overlap(...)   circles_overlap(...)   point_in_rect(...)
find_path(grid, sx,sy, ex,ey)     # A* pathfinding
line_of_sight(grid, x1,y1, x2,y2) # wall-aware visibility
```

## Documentation

- [Language reference](docs/language.md) — syntax, types, control flow, functions, modules
- [Standard library](docs/stdlib.md) — every builtin function
- [CLI](docs/cli.md) — every `mko` command
- [Mako2D](docs/mako2d.md) · [Mako3D](docs/mako3d.md) · [Inputs](docs/inputs.md) · [Audio](docs/audio.md) · [MakoUI](docs/makoui.md)
- [Game development guide](docs/games.md) — delta time, collision, AI patterns, positional audio

## Building from source

```bash
./build.sh dev        # debug build
./build.sh release    # optimized single-file binary → bin/
./build.sh install    # release build + install to ~/.local/bin
./build.sh clean
```

Implementation: a tree-walk interpreter in C# / .NET 8. Graphics via raylib (Raylib-cs) and Dear ImGui (ImGui.NET + Silk.NET).

## License

MIT
