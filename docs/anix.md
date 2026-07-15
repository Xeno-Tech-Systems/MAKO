# ANIX — build Abora system plans with MAKO

The `ANIX` package builds ANIX Plan v1 JSON. It cannot modify system state or
run privileged commands; the ANIX core validates and applies its output.

```mako
using ANIX;

main() {
    ANIX.set("hostname", "everest");
    ANIX.enable("bluetooth");
    ANIX.package("firefox");
    ANIX.finish();
}
```

Run through ANIX v2:

```bash
anix language use mako
anix plan workstation.mko
anix run workstation.mko
```

| Function | Plan operation |
|---|---|
| `set(key, value)` | `set` |
| `enable(feature)` | `enable` |
| `disable(feature)` | `disable` |
| `package(name)` | `package.add` |
| `remove_package(name)` | `package.remove` |
| `plan()` | Returns the plan as a MAKO dictionary |
| `finish()` | Writes the final JSON plan for the ANIX adapter |

Keep `ANIX.finish()` as the final statement and do not print other text when a
script is intended for `anix run`; adapter standard output is the plan channel.
