# MAKO Language Reference

**Version:** 0.02  
**File extension:** `.mko`

---

## Overview

MAKO is a dynamically-typed, imperative scripting language. Programs are easy to read, easy to type, and easy to remember. MAKO uses C-style braces `{}`, requires no boilerplate, and runs with a single command.

---

## File structure

A MAKO program is a `.mko` file. At the top level you can have a script declaration, a namespace declaration, use imports, function definitions, and a main block.

```mako
script "My App";         // optional — human-readable name
namespace MyLib;         // optional — makes this file a module

use "mathlib.mko";       // import another module (optional, repeatable)

fn helper(x) {           // function definitions (optional, repeatable)
    return x * 2;
}

main() {                 // entry point — code runs here
    print "Hello!";
}
```

Only `main()` is required for a runnable program. Library files (modules) omit `main()` and use `namespace` instead.

---

## Comments

```mako
# hash comment (Python-style)
// double-slash comment
/* block comment
   spans multiple lines */

print "hello"; # inline comment
```

---

## Variables

Declare a variable by assigning to a name — no keyword needed:

```mako
name    = "MAKO";
version = 0.02;
active  = true;
nothing = none;
```

Names can contain letters, digits, and `_`, but must start with a letter or `_`.  
Variables can be reassigned and can change type freely.

### Constants

Use `const` for values that must never change:

```mako
const PI      = 3.14159;
const APP     = "MAKO";
const MAX_LEN = 100;
```

Attempting to reassign a `const` is a runtime error.

---

## Types

| Type    | Example             | Notes                                        |
|---------|---------------------|----------------------------------------------|
| String  | `"hello"`           | Double-quoted. Supports escape sequences.    |
| Number  | `42`, `3.14`        | All numbers are 64-bit floats internally.    |
| Boolean | `true`, `false`     | Literal keywords.                            |
| List    | `[1, "two", true]`  | Ordered, mixed-type, dynamic.                |
| None    | `none`              | Represents "no value".                       |

---

## Strings

```mako
greeting = "Hello, world!";
```

### Escape sequences

| Sequence | Meaning      |
|----------|--------------|
| `\n`     | Newline      |
| `\t`     | Tab          |
| `\"`     | Double quote |
| `\\`     | Backslash    |
| `\r`     | Carriage return |

### Concatenation

`+` joins strings. If either side is a string, the other side is coerced:

```mako
print "Score: " + 42;       // Score: 42
print "Pi is " + 3.14159;   // Pi is 3.14159
```

### String interpolation

Embed any expression directly inside a string with `{expr}`:

```mako
name = "Alice";
age  = 30;
print "Hello, {name}! You are {age} years old.";
print "Next year you will be {age + 1}.";
print "Pi = {3.14159}";
```

Use `{{` and `}}` for literal braces:

```mako
print "Use {{braces}} like this";   // Use {braces} like this
```

---

## Numbers

All numbers are 64-bit floats. Whole numbers print without a decimal point.

### Arithmetic

| Operator | Meaning        |
|----------|----------------|
| `a + b`  | Addition       |
| `a - b`  | Subtraction    |
| `a * b`  | Multiplication |
| `a / b`  | Division       |
| `a % b`  | Modulo         |
| `-a`     | Negation       |

### Compound assignment

```mako
x = 10;
x += 5;   // x = 15
x -= 3;   // x = 12
x *= 2;   // x = 24
x /= 4;   // x = 6
```

---

## Booleans

```mako
active = true;
done   = false;
```

### Truthiness

| Value                | Treated as |
|----------------------|------------|
| `true`               | true       |
| `false`              | false      |
| `0`                  | false      |
| Any non-zero number  | true       |
| `""`                 | false      |
| Any non-empty string | true       |
| `[]`                 | false      |
| Any non-empty list   | true       |
| `none`               | false      |

---

## Comparisons

| Operator | Meaning               |
|----------|-----------------------|
| `==`     | Equal                 |
| `!=`     | Not equal             |
| `<`      | Less than             |
| `>`      | Greater than          |
| `<=`     | Less than or equal    |
| `>=`     | Greater than or equal |

`==` works for any type, including lists.

---

## Logical operators

| Operator   | Meaning                            |
|------------|------------------------------------|
| `a and b`  | True if both are truthy (short-circuits) |
| `a or b`   | True if either is truthy (short-circuits) |
| `not a`    | Inverts truthiness                 |
| `!a`       | Same as `not a`                    |

```mako
if x > 0 and x < 100 {
    print "in range";
}

result = value or "default";   // returns "default" if value is falsy
```

---

## Operator precedence

From highest to lowest:

| Level | Operators                               |
|-------|-----------------------------------------|
| 1     | `!` `-` (unary)                         |
| 2     | `*` `/` `%`                             |
| 3     | `+` `-`                                 |
| 4     | `==` `!=` `<` `>` `<=` `>=`            |
| 5     | `and` `or`                              |

Use `()` to override:

```mako
result = (2 + 3) * 4;   // 20, not 14
```

---

## If / else if / else

```mako
if condition {
    // ...
} else if other {
    // ...
} else {
    // ...
}
```

Conditions do not need parentheses.

```mako
score = 85;
if score >= 90      { print "A"; }
else if score >= 80 { print "B"; }
else if score >= 70 { print "C"; }
else                { print "Below C"; }
```

---

## While loops

```mako
i = 0;
while i < 5 {
    print i;
    i += 1;
}
```

---

## For loops

Iterate over a list:

```mako
fruits = ["apple", "banana", "cherry"];
for fruit in fruits {
    print fruit;
}
```

Use `range()` to loop over numbers:

```mako
for i in range(5)          { print i; }        // 0 1 2 3 4
for i in range(1, 6)       { print i; }        // 1 2 3 4 5
for i in range(0, 10, 2)   { print i; }        // 0 2 4 6 8
for i in range(10, 0, -1)  { print i; }        // 10 9 8 ... 1
```

---

## Break and continue

```mako
i = 0;
while i < 10 {
    i += 1;
    if i % 2 == 0 { continue; }   // skip even numbers
    if i == 7     { break; }      // stop at 7
    print i;
}
```

Both work inside `while` and `for` loops.

---

## Functions

Define with `fn`, call by name:

```mako
fn add(a, b) {
    return a + b;
}

result = add(3, 4);   // 7
```

Functions can call themselves recursively:

```mako
fn factorial(n) {
    if n <= 1 { return 1; }
    return n * factorial(n - 1);
}

print factorial(10);   // 3628800
```

Functions have their own scope — variables inside do not leak out. Parameters shadow outer variables with the same name.

`return` with no value returns `none`. Reaching the end of a function also returns `none`.

---

## Lists

```mako
nums   = [1, 2, 3, 4, 5];
mixed  = ["hello", 42, true, none];
empty  = [];
```

### Indexing

```mako
print nums[0];    // 1  (first)
print nums[-1];   // 5  (last)
print nums[-2];   // 4  (second from last)
```

### Assignment

```mako
nums[0] = 99;
```

### Concatenation

```mako
a = [1, 2];
b = [3, 4];
c = a + b;   // [1, 2, 3, 4]
```

---

## Print

```mako
print "Hello";        // with newline
printnl "Hello ";     // without newline (continues on same line)
printnl "World";
print "";             // just a newline
```

---

## Input

```mako
name = input "Enter your name: ";
```

Always returns a string. For numeric input, coercion happens automatically in math expressions, or use `to_num()` explicitly.

---

## Run

Execute a shell command:

```mako
run "echo hello";
run "ls -lh " + folder;
```

Runs in `/bin/sh`, blocks until complete.

---

## Namespaces and modules

### Defining a module

```mako
// mathlib.mko
namespace Math;

fn add(a, b)  { return a + b; }
fn sub(a, b)  { return a - b; }
fn clamp(v, lo, hi) {
    if v < lo { return lo; }
    if v > hi { return hi; }
    return v;
}
```

### Using a module

```mako
script "My App";
use "mathlib.mko";

main() {
    print Math.add(10, 5);
    print Math.clamp(99, 0, 10);
}
```

- `use` paths are relative to the script being run
- The imported file must have a `namespace` declaration
- Functions are called as `Namespace.funcname(args)`
- Multiple `use` lines are allowed

---

## Built-in functions

### Type and conversion

| Function     | Description                              |
|--------------|------------------------------------------|
| `type(x)`    | Returns `"string"`, `"number"`, `"bool"`, `"list"`, or `"none"` |
| `to_num(x)`  | Converts a string to a number            |
| `to_str(x)`  | Converts any value to a string           |

### Math

| Function       | Description                      |
|----------------|----------------------------------|
| `abs(x)`       | Absolute value                   |
| `floor(x)`     | Round down                       |
| `ceil(x)`      | Round up                         |
| `round(x)`     | Round to nearest (half up)       |
| `sqrt(x)`      | Square root                      |
| `pow(x, y)`    | x to the power of y              |
| `max(a, b)`    | Larger of two numbers            |
| `min(a, b)`    | Smaller of two numbers           |

### Range

| Function                    | Description                                |
|-----------------------------|--------------------------------------------|
| `range(n)`                  | List `[0, 1, ..., n-1]`                   |
| `range(start, stop)`        | List from start up to (not including) stop |
| `range(start, stop, step)`  | With custom step (can be negative)         |

### String

| Function                     | Description                                  |
|------------------------------|----------------------------------------------|
| `len(s)`                     | Length of string                             |
| `upper(s)`                   | Uppercase                                    |
| `lower(s)`                   | Lowercase                                    |
| `trim(s)`                    | Remove leading/trailing whitespace           |
| `contains(s, sub)`           | True if `sub` is found in `s`               |
| `starts_with(s, prefix)`     | True if `s` starts with `prefix`            |
| `ends_with(s, suffix)`       | True if `s` ends with `suffix`              |
| `replace(s, old, new)`       | Replace all occurrences of `old` with `new` |
| `split(s, sep)`              | Split string into a list                     |
| `join(list, sep)`            | Join list elements into a string             |

### List

| Function         | Description                                    |
|------------------|------------------------------------------------|
| `len(list)`      | Number of elements                             |
| `push(list, val)`| Append a value (mutates in place)              |
| `pop(list)`      | Remove and return last element                 |
| `first(list)`    | First element                                  |
| `last(list)`     | Last element                                   |
| `reverse(list)`  | Return a new reversed list                     |
| `has(list, val)` | True if value is in the list                   |

### Program control

| Function           | Description                                  |
|--------------------|----------------------------------------------|
| `assert(cond)`     | Throws an error if `cond` is falsy           |
| `assert(cond, msg)`| Throws an error with `msg` if `cond` is falsy |
| `exit()`           | Exit with code 0                             |
| `exit(code)`       | Exit with given code                         |

---

## Semicolons

Every statement ends with `;`. Blocks `{ }` do not.

```mako
name = "Alice";
print "Hi, {name}";

if name == "Alice" {
    print "Found her";
}
```

---

## Keywords

Reserved and cannot be used as variable names:

`script` `namespace` `main` `fn` `return`  
`const` `if` `else` `while` `for` `in`  
`break` `continue` `print` `printnl` `input`  
`run` `use` `and` `or` `not`  
`true` `false` `none`

---

## Full example

```mako
script "Student Grades";

fn grade(score) {
    if score >= 90 { return "A"; }
    if score >= 80 { return "B"; }
    if score >= 70 { return "C"; }
    if score >= 60 { return "D"; }
    return "F";
}

fn average(scores) {
    total = 0;
    for s in scores { total += s; }
    return total / len(scores);
}

main() {
    scores = [92, 85, 78, 96, 61];
    avg    = average(scores);

    print "Scores: {scores}";
    print "Average: {round(avg)}";
    print "Grade: {grade(avg)}";

    print "--- Individual grades ---";
    for s in scores {
        print "  {s} → {grade(s)}";
    }
}
```
