# Save — local game data and unlockables

`Save` stores progress in the current user's local application-data folder.
Choose a stable game name once, then use normal MAKO values. Changes save
automatically and atomically.

```mako
using Save;

Save.open("slime-adventure");
Save.set("high_score", 1200);
Save.set("settings", {"volume": 0.7});

Save.unlock("golden_slime");
if Save.unlocked("golden_slime") { ... }
```

| Function | What it does |
|---|---|
| `open(game)` | Opens this game's local save file and returns its path |
| `set(key, value)` | Stores a string, number, boolean, list, dict, or `none` |
| `get(key, default=none)` | Reads a value or returns the default |
| `has(key)` | Checks whether a key exists |
| `remove(key)` | Removes one value |
| `all()` | Returns all local data |
| `unlock(name)` | Permanently unlocks a named item once |
| `unlocked(name)` | Checks a named unlockable |
| `unlocks()` | Lists every unlock |
| `path()` | Returns the local save location |
| `clear()` | Deletes all progress and the save file |

On Linux the default location is `~/.local/share/mko/saves/`. Windows and
macOS use their normal per-user application-data locations. Foundry builds do
not put saves beside the executable, so updating a game does not erase them.
