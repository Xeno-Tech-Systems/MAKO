using System.Text;

namespace Mako;

/// Emits a small, freestanding System V AMD64 backend for kernel-profile MIR.
/// Values currently occupy one 64-bit stack slot; narrower integer layout is
/// preserved by the type system and will be tightened during register lowering.
static class X64AssemblyEmitter
{
    private static readonly string[] ArgumentRegisters =
        ["rdi", "rsi", "rdx", "rcx", "r8", "r9"];

    public static string Emit(MirProgram program)
    {
        var output = new StringBuilder();
        output.AppendLine(".intel_syntax noprefix");
        output.AppendLine(".text");
        foreach (var function in program.Functions.Where(fn => fn.Name != "__mako_main"))
            EmitFunction(output, function);
        output.AppendLine(".section .note.GNU-stack,\"\",@progbits");
        return output.ToString();
    }

    private static void EmitFunction(StringBuilder output, MirFunction function)
    {
        if (function.Parameters.Count > ArgumentRegisters.Length)
            throw new InvalidOperationException($"native x86_64: '{function.Name}' has more than six parameters");

        var slots = CollectSlots(function);
        var types = CollectTypes(function);
        var frameSize = Align16(slots.Count * 8);
        var returnLabel = $".L_{function.Name}_return";
        output.Append("\n.globl ").AppendLine(function.Name);
        output.Append(".type ").Append(function.Name).AppendLine(", @function");
        output.Append(function.Name).AppendLine(":");
        output.AppendLine("    push rbp");
        output.AppendLine("    mov rbp, rsp");
        if (frameSize > 0) output.Append("    sub rsp, ").AppendLine(frameSize.ToString());
        for (var i = 0; i < function.Parameters.Count; i++)
            output.Append("    mov QWORD PTR ").Append(Address(slots, $"%{function.Parameters[i].Name}"))
                .Append(", ").AppendLine(ArgumentRegisters[i]);
        output.Append("    jmp ").AppendLine(Label(function, function.Blocks[0].Label));

        foreach (var block in function.Blocks)
        {
            output.Append(Label(function, block.Label)).AppendLine(":");
            foreach (var instruction in block.Instructions)
                EmitInstruction(output, function, instruction, slots, types);
            EmitTerminator(output, function, block.Terminator!, slots, returnLabel);
        }

        output.Append(returnLabel).AppendLine(":");
        output.AppendLine("    leave");
        output.AppendLine("    ret");
        output.Append(".size ").Append(function.Name).Append(", .-").AppendLine(function.Name);
    }

    private static Dictionary<string, int> CollectSlots(MirFunction function)
    {
        var names = new HashSet<string>(function.Parameters.Select(p => $"%{p.Name}"), StringComparer.Ordinal);
        foreach (var instruction in function.Blocks.SelectMany(block => block.Instructions))
        {
            if (instruction.Result?.StartsWith('%') == true) names.Add(instruction.Result);
            if (instruction.Op is "alloc" or "alloc.const" && instruction.Arguments.Count > 0)
                names.Add(instruction.Arguments[0]);
        }
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        var index = 1;
        foreach (var name in names.Order(StringComparer.Ordinal)) result[name] = index++ * 8;
        return result;
    }

    private static Dictionary<string, string> CollectTypes(MirFunction function)
    {
        var result = function.Parameters.ToDictionary(
            parameter => $"%{parameter.Name}", parameter => parameter.Type,
            StringComparer.Ordinal);
        foreach (var instruction in function.Blocks.SelectMany(block => block.Instructions))
        {
            if (instruction.Result != null) result[instruction.Result] = instruction.Type;
            if (instruction.Op is "alloc" or "alloc.const" && instruction.Arguments.Count > 0)
                result[instruction.Arguments[0]] = instruction.Type;
        }
        return result;
    }

    private static void EmitInstruction(StringBuilder output, MirFunction function,
        MirInstruction instruction, Dictionary<string, int> slots,
        Dictionary<string, string> types)
    {
        var op = instruction.Op;
        if (op is "param" or "alloc" or "alloc.const") return;
        if (op == "eval") return;
        if (op == "const")
        {
            var value = instruction.Arguments[0] switch { "true" => "1", "false" or "none" => "0", var v => v };
            output.Append("    mov rax, ").AppendLine(value);
            StoreResult(output, instruction, slots);
            return;
        }
        if (op == "load")
        {
            Load(output, "rax", instruction.Arguments[0], slots);
            StoreResult(output, instruction, slots);
            return;
        }
        if (op == "store")
        {
            Load(output, "rax", instruction.Arguments[1], slots);
            output.Append("    mov QWORD PTR ").Append(Address(slots, instruction.Arguments[0])).AppendLine(", rax");
            return;
        }
        if (op.StartsWith("binary.", StringComparison.Ordinal) ||
            op.StartsWith("logical.", StringComparison.Ordinal))
        {
            EmitBinary(output, instruction, slots, types);
            return;
        }
        if (op.StartsWith("unary.", StringComparison.Ordinal))
        {
            Load(output, "rax", instruction.Arguments[0], slots);
            switch (op[6..])
            {
                case "-": output.AppendLine("    neg rax"); break;
                case "!": case "not":
                    output.AppendLine("    test rax, rax");
                    output.AppendLine("    sete al");
                    output.AppendLine("    movzx rax, al");
                    break;
                default: throw Unsupported(function, instruction);
            }
            StoreResult(output, instruction, slots);
            return;
        }
        if (op.StartsWith("call.", StringComparison.Ordinal))
        {
            if (op.StartsWith("call.abi_syscall", StringComparison.Ordinal))
            {
                EmitAbiSyscall(output, instruction, slots);
                return;
            }
            if (op.StartsWith("call.volatile_", StringComparison.Ordinal))
            {
                EmitVolatile(output, instruction, slots);
                return;
            }
            if (op.StartsWith("call.vptr_", StringComparison.Ordinal))
            {
                EmitVolatilePointer(output, instruction, slots);
                return;
            }
            if (instruction.Arguments.Count > ArgumentRegisters.Length) throw Unsupported(function, instruction);
            for (var i = 0; i < instruction.Arguments.Count; i++)
                Load(output, ArgumentRegisters[i], instruction.Arguments[i], slots);
            output.Append("    call ").AppendLine(op[5..]);
            StoreResult(output, instruction, slots);
            return;
        }
        if (op.StartsWith("convert.", StringComparison.Ordinal))
        {
            Load(output, "rax", instruction.Arguments[0], slots);
            StoreResult(output, instruction, slots);
            return;
        }
        throw Unsupported(function, instruction);
    }

    private static void EmitAbiSyscall(StringBuilder output, MirInstruction instruction,
        Dictionary<string, int> slots)
    {
        var name = instruction.Op[5..];
        var argumentCount = name[^1] - '0';
        if (argumentCount is < 0 or > 5 || instruction.Arguments.Count != argumentCount + 1)
            throw new InvalidOperationException($"native x86_64: invalid ABI intrinsic '{name}'");

        // MAKO-ABI follows the Linux-style syscall register convention rather
        // than the System V function convention: number in rax, then rdi,
        // rsi, rdx, r10 and r8. int 0x80 returns the result in rax.
        string[] registers = ["rdi", "rsi", "rdx", "r10", "r8"];
        Load(output, "rax", instruction.Arguments[0], slots);
        for (var i = 0; i < argumentCount; i++)
            Load(output, registers[i], instruction.Arguments[i + 1], slots);
        output.AppendLine("    int 0x80");
        StoreResult(output, instruction, slots);
    }

    private static void EmitVolatile(StringBuilder output, MirInstruction instruction,
        Dictionary<string, int> slots)
    {
        var name = instruction.Op[5..];
        var isLoad = name.StartsWith("volatile_load_", StringComparison.Ordinal);
        var width = name[(name.LastIndexOf('_') + 2)..];
        Load(output, "rax", instruction.Arguments[0], slots);
        if (isLoad)
        {
            output.AppendLine(width switch
            {
                "8" => "    movzx eax, BYTE PTR [rax]",
                "16" => "    movzx eax, WORD PTR [rax]",
                "32" => "    mov eax, DWORD PTR [rax]",
                "64" => "    mov rax, QWORD PTR [rax]",
                _ => throw new InvalidOperationException($"native x86_64: invalid volatile width '{width}'"),
            });
            StoreResult(output, instruction, slots);
            return;
        }

        Load(output, "r10", instruction.Arguments[1], slots);
        output.AppendLine(width switch
        {
            "8" => "    mov BYTE PTR [rax], r10b",
            "16" => "    mov WORD PTR [rax], r10w",
            "32" => "    mov DWORD PTR [rax], r10d",
            "64" => "    mov QWORD PTR [rax], r10",
            _ => throw new InvalidOperationException($"native x86_64: invalid volatile width '{width}'"),
        });
        output.AppendLine("    xor eax, eax");
        StoreResult(output, instruction, slots);
    }

    private static void EmitVolatilePointer(StringBuilder output, MirInstruction instruction,
        Dictionary<string, int> slots)
    {
        var name = instruction.Op[5..];
        var width = int.Parse(name[(name.LastIndexOf('u') + 1)..]);
        var bytes = width / 8;
        Load(output, "rax", instruction.Arguments[0], slots);
        if (name.StartsWith("vptr_from_", StringComparison.Ordinal))
        {
            StoreResult(output, instruction, slots);
            return;
        }
        if (name.StartsWith("vptr_offset_", StringComparison.Ordinal))
        {
            Load(output, "r10", instruction.Arguments[1], slots);
            if (bytes > 1) output.Append("    imul r10, ").AppendLine(bytes.ToString());
            output.AppendLine("    add rax, r10");
            StoreResult(output, instruction, slots);
            return;
        }
        if (name.StartsWith("vptr_read_", StringComparison.Ordinal))
        {
            output.AppendLine(width switch
            {
                8 => "    movzx eax, BYTE PTR [rax]",
                16 => "    movzx eax, WORD PTR [rax]",
                32 => "    mov eax, DWORD PTR [rax]",
                64 => "    mov rax, QWORD PTR [rax]",
                _ => throw new InvalidOperationException($"native x86_64: invalid pointer width '{width}'"),
            });
            StoreResult(output, instruction, slots);
            return;
        }
        if (name.StartsWith("vptr_write_", StringComparison.Ordinal))
        {
            Load(output, "r10", instruction.Arguments[1], slots);
            output.AppendLine(width switch
            {
                8 => "    mov BYTE PTR [rax], r10b",
                16 => "    mov WORD PTR [rax], r10w",
                32 => "    mov DWORD PTR [rax], r10d",
                64 => "    mov QWORD PTR [rax], r10",
                _ => throw new InvalidOperationException($"native x86_64: invalid pointer width '{width}'"),
            });
            output.AppendLine("    xor eax, eax");
            StoreResult(output, instruction, slots);
            return;
        }
        throw new InvalidOperationException($"native x86_64: unsupported pointer intrinsic '{name}'");
    }

    private static void EmitBinary(StringBuilder output, MirInstruction instruction,
        Dictionary<string, int> slots, Dictionary<string, string> types)
    {
        Load(output, "rax", instruction.Arguments[0], slots);
        Load(output, "r10", instruction.Arguments[1], slots);
        var operation = instruction.Op[(instruction.Op.IndexOf('.') + 1)..];
        var operandType = types.GetValueOrDefault(instruction.Arguments[0], instruction.Type);
        switch (operation)
        {
            case "+": output.AppendLine("    add rax, r10"); break;
            case "-": output.AppendLine("    sub rax, r10"); break;
            case "*": output.AppendLine("    imul rax, r10"); break;
            case "and": output.AppendLine("    and rax, r10"); break;
            case "or": output.AppendLine("    or rax, r10"); break;
            case "/": case "%":
                output.AppendLine(IsSigned(operandType) ? "    cqo" : "    xor rdx, rdx");
                output.AppendLine(IsSigned(operandType) ? "    idiv r10" : "    div r10");
                if (operation == "%") output.AppendLine("    mov rax, rdx");
                break;
            case "==": case "!=": case "<": case "<=": case ">": case ">=":
                output.AppendLine("    cmp rax, r10");
                output.Append("    set").Append(Condition(operation, operandType)).AppendLine(" al");
                output.AppendLine("    movzx rax, al");
                break;
            default: throw new InvalidOperationException($"native x86_64: unsupported binary operation '{operation}'");
        }
        StoreResult(output, instruction, slots);
    }

    private static void EmitTerminator(StringBuilder output, MirFunction function,
        MirTerminator terminator, Dictionary<string, int> slots, string returnLabel)
    {
        switch (terminator.Op)
        {
            case "return":
                if (terminator.Arguments.Count > 0 && terminator.Arguments[0] is not "none" and not "implicit_none")
                    Load(output, "rax", terminator.Arguments[0], slots);
                else output.AppendLine("    xor eax, eax");
                output.Append("    jmp ").AppendLine(returnLabel);
                break;
            case "jump": output.Append("    jmp ").AppendLine(Label(function, terminator.Targets[0])); break;
            case "branch":
                Load(output, "rax", terminator.Arguments[0], slots);
                output.AppendLine("    test rax, rax");
                output.Append("    jne ").AppendLine(Label(function, terminator.Targets[0]));
                output.Append("    jmp ").AppendLine(Label(function, terminator.Targets[1]));
                break;
            default: throw new InvalidOperationException($"native x86_64: unsupported terminator '{terminator.Op}' in '{function.Name}'");
        }
    }

    private static void Load(StringBuilder output, string register, string value,
        Dictionary<string, int> slots)
    {
        if (value.StartsWith('%'))
            output.Append("    mov ").Append(register).Append(", QWORD PTR ").AppendLine(Address(slots, value));
        else
            output.Append("    mov ").Append(register).Append(", ").AppendLine(value);
    }

    private static void StoreResult(StringBuilder output, MirInstruction instruction,
        Dictionary<string, int> slots)
    {
        if (instruction.Result != null)
            output.Append("    mov QWORD PTR ").Append(Address(slots, instruction.Result)).AppendLine(", rax");
    }

    private static string Address(Dictionary<string, int> slots, string name) =>
        slots.TryGetValue(name, out var offset) ? $"[rbp - {offset}]" :
        throw new InvalidOperationException($"native x86_64: missing stack slot for '{name}'");
    private static string Label(MirFunction function, string block) => $".L_{function.Name}_{block}";
    private static int Align16(int value) => (value + 15) & ~15;
    private static bool IsSigned(string type) => type.StartsWith('i') || type == "isize";
    private static string Condition(string op, string type)
    {
        var signed = IsSigned(type);
        return op switch { "==" => "e", "!=" => "ne", "<" => signed ? "l" : "b", "<=" => signed ? "le" : "be", ">" => signed ? "g" : "a", ">=" => signed ? "ge" : "ae", _ => throw new InvalidOperationException() };
    }
    private static Exception Unsupported(MirFunction function, MirInstruction instruction) =>
        new InvalidOperationException($"native x86_64: unsupported instruction '{instruction.Op}' in '{function.Name}'");
}
