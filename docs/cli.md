# The `mko` CLI

## Running scripts

```bash
mko script.mko            # shorthand
mko run script.mko        # explicit form
./script.mko              # with a #!/usr/bin/env mko shebang + chmod +x
```

Script paths resolve in order: as given → `~/.local/share/mko/<path>` →
`~/.local/share/mko/examples/<path>` → `~/.local/share/mko/scripts/<path>`.
The `.mko` extension is optional for the global locations, so `mko pong`
works from any directory.

## REPL

```bash
mko repl
```

Interactive prompt. Expressions print their value automatically; functions
and constants persist across lines. `exit` or Ctrl+C to quit.

## Formatter

```bash
mko fmt script.mko            # format in place
mko fmt script.mko --check    # exit 1 if the file needs formatting
```

AST-based: normalizes indentation (4 spaces), spacing, and brace style.
Preserves standalone comments and template strings exactly.

## Static analysis and compiler IR

```bash
mko check script.mko          # static types plus lint checks
mko check script.mko --kernel # enforce the freestanding kernel subset
mko ir script.mko          # print typed high-level IR to stdout
mko mir script.mko         # print basic-block middle IR to stdout
mko mir script.mko --opt   # validate and optimize before printing
mko native script.mko --kernel -o module.S # emit freestanding x86_64 assembly
```

`mko ir` parses and type-checks the program, then lowers it to `mako.hir 1`.
The output includes normalized bindings, typed expressions, function
signatures, struct layouts, collection operations, and structured control-flow
regions. It is intended for compiler/backend development and can be redirected
to a file for inspection. Programs with static type errors are not lowered.

`mko mir` performs the next lowering stage. `mako.mir 1` contains flat basic
blocks, typed temporaries, explicit local/global allocation, loads, stores,
numeric and collection conversions, branches, loop back-edges, iterator
operations, exception edges, and terminators. This is the representation meant
to feed optimization and native instruction selection. `--opt` folds constant
arithmetic, comparisons, conversions, and constant branches, then removes dead
pure temporaries and unreachable blocks. MIR is structurally validated before
and after optimization.

`--kernel` adds a deliberately narrow freestanding profile. Kernel functions
must have explicit integer, boolean, or `none` signatures; locals must be typed
when first declared; and code may call user-defined functions or explicit
freestanding intrinsics. Host
packages, allocation-backed collections, strings, exceptions, shell/process
features, and dynamic values are rejected. This profile is the contract for
the native kernel backend while object emission is being built.

`mko native` lowers optimized MIR to System V AMD64 assembly. The initial
backend covers the kernel profile's scalar stack values, arithmetic,
comparisons, branches, loops, returns, and calls with up to six arguments.
It also lowers volatile/typed-pointer memory operations and `abi_syscall0`
through `abi_syscall5` directly, including the MAKO-ABI `r10` fourth argument.
Local `use "module.mko"` dependencies are recursively resolved, checked, and
emitted as namespaced native symbols in the same output file.
Assemble the output with an ELF-capable assembler and link the resulting object
into a freestanding image.

## Tests

```bash
mko test              # run every *.mko file under ./tests/
mko test some/dir      # or a custom directory
```

Each test file is a normal MAKO script that uses `assert(cond, msg)`. Before
execution, the runner type-checks and lowers both the original and formatted
source, then validates and optimizes both forms and verifies that formatting
produces identical typed HIR, MIR, and optimized MIR. A file
**passes** if those compiler checks and execution all complete; it **fails**
if any check or assertion fails. Exit code is `0` only if every file passed —
wire it into CI as-is.

```
PASS  tests/lists.mko
FAIL  tests/dicts.mko: Assertion failed: merge — second wins (line 27)

4 passed, 1 failed — see FAIL lines above
```

## Packages

```bash
mko get somelib github:User/Repo    # install from GitHub
mko get somelib                     # install from the registry
mko list                            # show installed packages
mko cache clear                     # remove all cached packages
mko cache clear somelib             # remove one
```

Packages live in `~/.local/share/mko/packages/`. A package repo needs an
`index.mko` that declares a `namespace`.

In a script, `using name from "github:User/Repo";` fetches automatically on
first run.

## Foundry builds

```bash
mko foundry game.mko                  # open the MakoUI builder
mko foundry game.mko --term           # list target readiness in the terminal
mko build game.mko --target linux-x64 # direct/CI build
```

Foundry's first ready target produces a self-contained Linux x64 executable
folder. See [foundry.md](foundry.md) for project manifests, artifact layout,
and the Windows/AppImage/Android/macOS/Web/VR/console roadmap.

## Misc

```bash
mko version
mko help
```

## Install layout

`./build.sh install` puts things here:

| Path | What |
|---|---|
| `~/.local/bin/mko` | Launcher (sets LD_LIBRARY_PATH, runs the real binary) |
| `~/.local/share/mko/bin/` | `mko.bin` + native libraries (raylib, cimgui, glfw) |
| `~/.local/share/mko/examples/` | All example scripts + assets |
| `~/.local/share/mko/packages/` | Cached GitHub packages |
