# Models — named 3D objects

`Models` loads your `.glb` or `.obj` files and gives them readable names. It
also turns on Mako3D, so one import is enough.

```mako
using Models;

main() {
    Mako3D.init(900, 600, "My Model");
    Models.load("hero", "assets/hero.glb");

    # inside begin_3d / end_3d:
    Models.draw("hero", 0, 0, 0, 1, Mako3D.WHITE);
}
```

| Function | What it does |
|---|---|
| `load(name, path)` | Loads a `.glb` or `.obj` and remembers it by name |
| `has(name)` | Reports whether that name was loaded |
| `draw(name, x, y, z, scale=1, color=WHITE)` | Draws the named model |

Use `using Models;`, not `use 3dObjects;`: `use` loads another `.mko` script,
while `using` turns on a native package. A package name also cannot begin with
a number. The short name keeps those language rules consistent.
