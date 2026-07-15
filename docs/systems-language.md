# Typed and systems MAKO

MAKO is growing from an approachable interpreted language into a language that
can also target kernels, drivers, engines, servers, and native applications.
The systems work is incremental: existing untyped programs remain valid, while
annotations opt code into stricter checking.

## Typed variables

Add a type after a variable name. Once annotated, later assignments are checked
against that type too.

```mako
port: u16 = 8080;
port = 9000;
```

`mko run` and `mko check` validate integer literals against their declared
range. For example, `byte: u8 = 300;` is rejected before execution.

## Typed functions

Parameters and return values can be checked without adding declaration
boilerplate to local variables:

```mako
fn add(left: i32, right: i32) -> i32 {
    return left + right;
}
```

The checker validates typed arguments, return expressions, and whether every
path through a non-`none` function returns a value. Parameters may remain
untyped when a dynamic boundary is intentional.

## Typed structs

Fields may carry types, and struct literals and field assignments are checked:

```mako
struct Pixel { r: u8, g: u8, b: u8 }

main() {
    color: Pixel = Pixel { r: 255, g: 80, b: 40 };
}
```

Untyped fields are still supported, so existing structs do not need to change.

## Typed collections

Lists and dictionaries accept type arguments. Type expressions may nest:

```mako
bytes: list<u8> = [16, 32, 64];
pages: dict<string, list<u8>> = {"boot": bytes};
```

The checker validates literal contents, indexed reads and writes, dictionary
keys, function arguments and returns, and loop item types. Mutable collections
are invariant: a `list<u8>` cannot be silently widened to `list<number>`,
because wider code could then insert a value that does not fit in a byte.

Unparameterized `list` and `dict` remain available as dynamic collection types.

## Initial type set

- Integers: `i8`, `i16`, `i32`, `i64`, `isize`, `u8`, `u16`, `u32`, `u64`, `usize`
- Floating point: `f32`, `f64`
- Existing values: `number`, `bool`, `string`, `none`, `list`, `dict`, `fn`
- Typed collections: `list<T>` and `dict<K, V>`, including nested forms
- User-defined struct names
- `dynamic` for an explicit unchecked boundary

Compatibility aliases include `int` (`i64`), `float`/`double` (`f64`),
`str` (`string`), `boolean` (`bool`), and `void` (`none`).

## Current boundary

The interpreter still stores numeric values using its existing runtime
representation. Types are checked before execution and by `mko check`, but do
not yet define native layout or alter runtime storage. The next systems
milestones are a typed intermediate representation, fixed-width runtime values,
freestanding compilation, deterministic memory management, and a native code
backend.

## Typed high-level IR

The compiler frontend can now lower a valid program into typed, structured HIR:

```bash
mko ir program.mko
```

`mako.hir 1` preserves functions, struct layouts, bindings, typed expressions,
collections, calls, and structured control-flow regions. It deliberately sits
above machine instructions. `mko mir program.mko` performs the next lowering
stage: regions become basic blocks, mutable bindings become explicit storage,
and numeric, literal, dynamic, and collection conversions become instructions.
The resulting `mako.mir 1` is ready for optimization and native instruction
selection. The interpreter and compiler frontend share the same semantic
analysis rather than maintaining separate definitions of MAKO's types.

`mko mir program.mko --opt` runs the first target-independent pass pipeline.
It validates block targets, terminators, temporary definitions, and operand
references; folds constants and explicit numeric conversions; simplifies
constant branches; and removes dead pure instructions and unreachable blocks.
Optimized MIR is validated again before it is emitted.

## Freestanding kernel profile

Kernel-bound code can opt into the stricter compiler subset:

```bash
mko check kernel_module.mko --kernel
mko mir kernel_module.mko --opt
```

The profile currently permits typed functions, typed locals, integer and
boolean operations, calls between user functions, and structured branches and
loops. Every function parameter and return type must be explicit. It rejects
runtime-dependent features such as packages, strings, lists, dictionaries,
dynamic values, exceptions, process execution, and input/output builtins.

Passing this profile means the source and optimized MIR are accepted by the
initial freestanding backend. `mko native` now emits System V x86_64 assembly
for scalar arithmetic, control flow, and function calls; that assembly has been
linked into the reference C kernel and executed during boot. Direct ELF object
emission, tighter fixed-width storage, register allocation, relocations, and
kernel memory operations are the next backend stages.

### MAKO application ABI calls

Freestanding MKO applications can cross into the MAKO kernel without assembly
through `abi_syscall0` to `abi_syscall5`. The suffix is the number of syscall
arguments; the syscall number is always the first parameter, and every value
and result is a `u64`:

```mako
fn service_open(service: u64) -> u64 {
    return abi_syscall1(5, service);
}

fn file_open(storage: u64, name: u64, length: u64, create: u64) -> u64 {
    return abi_syscall4(9, storage, name, length, create);
}
```

The x86_64 backend emits `int 0x80` with the number in RAX, arguments in RDI,
RSI, RDX, R10, and R8, and the result in RAX. Argument counts and integer types
are checked by the compiler. These are raw architecture intrinsics intended as
the foundation for typed SDK functions; projects should call named wrappers so
the source remains independent of syscall numbers and registers.

Native compilation resolves local modules recursively with the normal MAKO
syntax:

```mako
use "sdk.mko";

fn start() -> u64 {
    return MakoAbi.getpid();
}
```

Each imported native module must declare a namespace and contain functions
only. Paths are relative to the importing file. Import cycles, missing files,
top-level module code, host packages, and duplicate linked symbols are rejected.
The compiler emits namespaced functions and their callers into one assembly
unit, so the system linker does not need MAKO-specific metadata.

### Volatile hardware access

Kernel-profile native code can perform explicitly sized volatile memory access:

```mako
fn write_vga_cell(address: u64, cell: u64) -> none {
    volatile_store_u16(address, cell);
}
```

The available intrinsics are `volatile_load_u8/u16/u32/u64` and
`volatile_store_u8/u16/u32/u64`. Loads take an integer address; stores take an
address and value. They lower directly to one machine memory operation and are
intended only for trusted freestanding code. They do not add bounds checks or
make an invalid physical address safe.

For typed device access, use `vptr<T>` with `T` equal to `u8`, `u16`, `u32`, or
`u64`. Raw addresses enter the pointer world through `vptr_from_u16(address)`
(with equivalent functions for the other widths). `vptr_offset_u16(pointer,
elements)` scales by the pointee size, while `vptr_read_u16` and
`vptr_write_u16` require the matching pointer type. This makes address creation
explicit and prevents accidental cross-width access while retaining the exact
volatile behavior required for memory-mapped hardware.

The reference kernel now uses these primitives for two real jobs: decoding
packed Multiboot2 memory-map entries and maintaining a 4 KiB physical-frame
allocator. MKO initializes a two-word allocator state (`next`, `end`) from an
available firmware range, aligns the first frame, checks arithmetic overflow
and exhaustion, updates the cursor through `vptr<u64>`, and returns physical
frame addresses to the C bootstrap.

MKO also constructs the reference kernel's active virtual-memory hierarchy. It
clears fresh PML4, PDPT, PD, and PT frames, creates 1,024 identity-mapped 4 KiB
leaf entries covering the first 4 MiB, and returns the PML4 frame for CR3. The
kernel switches away from the bootstrap's 2 MiB mappings, continues executing,
and asks native MKO to write and read a probe value through the new tables
before reporting `MKO_VIRTUAL_MEMORY_OK`.

The kernel now loads an x86_64 IDT after that switch. A live `int3` self-test
enters an assembly stub, preserves the general registers, calls C, and returns
with `iretq`; boot continues only if the handler count advances exactly once.
Vector 14 has a non-returning page-fault handler that captures CR2 and the
hardware error code and reports both over serial before halting.

Above that exception layer, the reference kernel now remaps the legacy PIC,
programs the PIT for 100 Hz, verifies that a hardware timer IRQ arrives, and
handles PS/2 keyboard scan codes. Those characters drive MakoBox, a small
BusyBox-style freestanding applet dispatcher with an interactive `mako#`
prompt and live memory, frame-allocator, paging, and timer diagnostics.
