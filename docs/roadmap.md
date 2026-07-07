# MAKO Roadmap

This document tracks what has been built and what is planned for future versions.

---

## v0.01 — First Working Interpreter ✅

- [x] `.mko` file extension
- [x] `script "Name";` declaration
- [x] `main() { }` entry point
- [x] `print expr;`
- [x] Variables — `name = value;`
- [x] String literals and joining with `+`
- [x] Number literals (integers and decimals)
- [x] Boolean literals — `true`, `false`
- [x] `none` keyword
- [x] `input "prompt"` — reads a line from stdin
- [x] Arithmetic — `+` `-` `*` `/`
- [x] Comparisons — `==` `!=` `<` `>` `<=` `>=`
- [x] `!` logical NOT
- [x] `if` / `else if` / `else`
- [x] `run "shell command";`
- [x] `//` line comments
- [x] Automatic type coercion (string + number, truthy checks)
- [x] Clean error messages with line numbers
- [x] `mako run`, `mako version`, `mako help` CLI

---

## v0.02 — Loops, Functions, Lists, Namespaces ✅

- [x] `while condition { }` loop
- [x] `for item in list { }` loop
- [x] `break` and `continue`
- [x] `fn name(params) { }` user-defined functions
- [x] `return expr;` — return values from functions
- [x] Recursive function calls
- [x] Lexical scoping — functions get their own scope
- [x] `and` / `or` — short-circuit logical operators
- [x] `not` keyword (alternative to `!`)
- [x] `-x` unary negation
- [x] `%` modulo operator
- [x] `+=` `-=` `*=` `/=` compound assignment
- [x] `const name = expr;` — immutable bindings
- [x] `[1, 2, 3]` list literals
- [x] `list[i]` indexing (negative indices supported)
- [x] `list[i] = val;` index assignment
- [x] `list + list` concatenation
- [x] `namespace Name;` — declare a module
- [x] `use "file.mko";` — import a module
- [x] `Namespace.func(args)` — namespaced calls
- [x] `"Hello, {name}!"` — string interpolation
- [x] `printnl expr;` — print without newline
- [x] `/* block comments */`
- [x] `#` hash comments
- [x] `range(n)` / `range(start, stop)` / `range(start, stop, step)`
- [x] `assert(cond, msg?)` — assertions
- [x] `exit(code?)` — exit the program
- [x] String built-ins: `len` `upper` `lower` `trim` `contains` `starts_with` `ends_with` `replace` `split` `join`
- [x] List built-ins: `len` `push` `pop` `first` `last` `reverse` `has`
- [x] Math built-ins: `abs` `floor` `ceil` `sqrt` `round` `pow` `max` `min`
- [x] Utility: `type` `to_num` `to_str`
- [x] Better error messages — shows offending source line with `^^^`
- [x] Single-file self-contained binary (`PublishSingleFile=true`)

---

## v0.03 — Tables and String Interpolation Enhancements

Structured data and richer string handling.

- [ ] Tables (key-value maps):
  ```mako
  person = {
      name = "Alice",
      age  = 30,
  };
  print person.name;
  person.age += 1;
  ```
- [ ] Nested tables and lists
- [ ] `for key in table { }` iteration
- [ ] `has(table, key)` — check if key exists
- [ ] `keys(table)` — list of keys
- [ ] Multi-line strings (triple-quote):
  ```mako
  msg = """
  Line one
  Line two
  """;
  ```
- [ ] `%=` modulo-assign
- [ ] `floor_div(a, b)` — integer division

---

## v0.04 — Standard Library Modules

Built-in modules accessible via `use`:

- [ ] `use "std:file";` — read and write files
  ```mako
  use "std:file";
  content = File.read("data.txt");
  File.write("out.txt", content);
  ```
- [ ] `use "std:time";` — timestamps, sleep
- [ ] `use "std:json";` — parse and build JSON
- [ ] `use "std:math";` — extended math (sin, cos, log, etc.)
- [ ] `use "std:http";` — simple HTTP GET/POST for scripting
- [ ] `use "std:env";` — environment variables, args

---

## v0.05 — Error Handling

- [ ] `try { } catch err { }` — catch runtime errors
- [ ] `throw "message";` — raise a custom error
- [ ] Error objects with `.message` and `.line`

---

## v1.0 — First Stable Release

- [ ] Full standard library (file, time, json, math, http, env)
- [ ] Bytecode compiler + VM for better performance
- [ ] Optional type hints (not enforced, just for tooling):
  ```mako
  name: string = "Alice";
  age:  number = 30;
  ```
- [ ] `mako check file.mko` — lint and hint validation
- [ ] `mako fmt file.mko` — auto-formatter
- [ ] Official documentation site
- [ ] Windows and macOS support in official releases
- [ ] Package manager (`mako add package-name`)

---

## Long term

- Compile to native binary via LLVM or QBE
- Embeddable as a scripting engine in C# / Rust applications
- MAKO for game scripting
- MAKO on the web via WASM
