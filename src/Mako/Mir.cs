using System.Text;

namespace Mako;

/// Lower, control-flow-oriented IR. Unlike HIR, MIR has flat basic blocks,
/// explicit local/global storage, temporaries, and terminators. It is still
/// target-independent; machine-specific selection comes next.
record MirProgram(List<HirStruct> Structs, List<MirFunction> Functions);
record MirFunction(string Name, List<HirParameter> Parameters, string ReturnType, List<MirBlock> Blocks);
record MirInstruction(string? Result, string Type, string Op, List<string> Arguments);
record MirTerminator(string Op, List<string> Arguments, List<string> Targets);

record MirBlock(string Label)
{
    public List<MirInstruction> Instructions { get; } = [];
    public MirTerminator? Terminator { get; set; }
}

readonly record struct MirValue(string Name, string Type);

static class MirLowerer
{
    public static MirProgram Lower(HirProgram hir)
    {
        var globalNames = hir.Globals.Select(global => global.Name).ToHashSet(StringComparer.Ordinal);
        var signatures = hir.Functions.ToDictionary(fn => fn.Name, StringComparer.Ordinal);
        var structures = hir.Structs.ToDictionary(item => item.Name, StringComparer.Ordinal);
        var functions = hir.Functions.Select(fn =>
        {
            var builder = new Builder(fn.Name, fn.Parameters, fn.ReturnType,
                globalNames, signatures, structures);
            builder.LowerBlock(fn.Body);
            return builder.Finish();
        }).ToList();

        var main = new Builder("__mako_main", [], "none", globalNames, signatures, structures);
        foreach (var global in hir.Globals)
        {
            var value = main.Coerce(main.LowerExpression(global.Initializer), global.Type);
            main.Emit(null, global.Type, "global.alloc", $"%{global.Name}");
            main.Emit(null, "none", "global.store", $"%{global.Name}", value.Name);
        }
        main.LowerBlock(hir.Main);
        functions.Add(main.Finish());
        return new MirProgram(hir.Structs, functions);
    }

    private sealed class Builder
    {
        private readonly string _name;
        private readonly List<HirParameter> _parameters;
        private readonly string _returnType;
        private readonly HashSet<string> _globals;
        private readonly Dictionary<string, HirFunction> _signatures;
        private readonly Dictionary<string, HirStruct> _structures;
        private readonly HashSet<string> _locals = new(StringComparer.Ordinal);
        private readonly List<MirBlock> _blocks = [];
        private readonly Stack<(string Break, string Continue)> _loops = [];
        private MirBlock _current;
        private int _tempId;
        private int _blockId;

        public Builder(string name, List<HirParameter> parameters, string returnType,
            HashSet<string> globals, Dictionary<string, HirFunction> signatures,
            Dictionary<string, HirStruct> structures)
        {
            _name = name;
            _parameters = parameters;
            _returnType = returnType;
            _globals = globals;
            _signatures = signatures;
            _structures = structures;
            _current = NewBlock("entry");
            foreach (var parameter in parameters)
            {
                _locals.Add(parameter.Name);
                Emit(null, parameter.Type, "param", $"%{parameter.Name}");
            }
        }

        public MirFunction Finish()
        {
            if (_current.Terminator == null)
                Terminate("return", _returnType == "none" ? ["none"] : ["implicit_none"]);
            return new MirFunction(_name, _parameters, _returnType, _blocks);
        }

        public void LowerBlock(HirBlock block)
        {
            foreach (var statement in block.Statements)
            {
                if (_current.Terminator != null) break;
                LowerStatement(statement);
            }
        }

        public MirValue LowerExpression(HirExpression expression)
        {
            if (expression.Op == "load")
            {
                var symbol = expression.Value ?? "unknown";
                var dot = symbol.IndexOf('.');
                if (dot > 0 && _locals.Contains(symbol[..dot]))
                    return EmitValue(expression.Type, "load.field", $"%{symbol[..dot]}", symbol[(dot + 1)..]);
                return _globals.Contains(symbol)
                    ? EmitValue(expression.Type, "global.load", $"%{symbol}")
                    : EmitValue(expression.Type, "load", $"%{symbol}");
            }

            var lowered = new List<(string? Label, MirValue Value)>();
            foreach (var argument in expression.Arguments)
            {
                var value = LowerExpression(argument.Value);
                lowered.Add((argument.Label, value));
            }

            if (expression.Op == "call" && expression.Value != null &&
                _signatures.TryGetValue(expression.Value, out var signature))
                for (var i = 0; i < lowered.Count && i < signature.Parameters.Count; i++)
                    lowered[i] = (lowered[i].Label,
                        Coerce(lowered[i].Value, signature.Parameters[i].Type));

            if (expression.Op == "struct" && expression.Value != null &&
                _structures.TryGetValue(expression.Value, out var structure))
                for (var i = 0; i < lowered.Count; i++)
                {
                    var field = structure.Fields.FirstOrDefault(item => item.Name == lowered[i].Label);
                    if (field != null)
                        lowered[i] = (lowered[i].Label, Coerce(lowered[i].Value, field.Type));
                }

            var arguments = lowered.Select(argument => argument.Label == null
                ? argument.Value.Name
                : $"{argument.Label}={argument.Value.Name}").ToList();

            var op = expression.Op switch
            {
                "literal" => "const",
                "binary" => $"binary.{expression.Value}",
                "logical" => $"logical.{expression.Value}",
                "unary" => $"unary.{expression.Value}",
                "call" => $"call.{expression.Value}",
                "method" => $"method.{expression.Value}",
                "field" => "load.field",
                "index" => "load.index",
                "struct" => $"struct.new.{expression.Value}",
                "template" => "template",
                "lambda" => "closure.new",
                _ => expression.Op,
            };
            if (expression.Op is "literal" or "template" && expression.Value != null)
                arguments.Insert(0, expression.Value);
            if (expression.Op == "lambda")
                arguments.Insert(0, "hir_body");
            return EmitValue(expression.Type, op, arguments.ToArray());
        }

        public MirValue Coerce(MirValue value, string expected)
        {
            if (expected == "dynamic" || expected == value.Type) return value;
            var op = value.Type switch
            {
                "int_literal" or "float_literal" => "convert.literal",
                "dynamic" => "cast.checked",
                "number" or "i8" or "i16" or "i32" or "i64" or "isize" or
                "u8" or "u16" or "u32" or "u64" or "usize" or "f32" or "f64"
                    => "convert.numeric",
                "list" or "dict" => "convert.collection",
                _ => "convert",
            };
            return EmitValue(expected, op, value.Name);
        }

        private void LowerStatement(HirStatement statement)
        {
            switch (statement.Op)
            {
                case "bind":
                case "const_bind":
                {
                    var value = LowerExpression(statement.Operands[0]);
                    var targetType = statement.Type ?? value.Type;
                    Bind(statement.Target!, targetType, Coerce(value, targetType),
                        statement.Op == "const_bind");
                    break;
                }
                case "print":
                case "print_no_line":
                case "run":
                case "eval":
                {
                    var values = statement.Operands.Select(LowerExpression).Select(value => value.Name).ToArray();
                    Emit(null, "none", statement.Op, values);
                    break;
                }
                case "store_index":
                {
                    var values = statement.Operands.Select(LowerExpression).ToList();
                    if (statement.Type != null && values.Count > 0)
                        values[^1] = Coerce(values[^1], statement.Type);
                    Emit(null, "none", "store.index",
                        new[] { $"%{statement.Target}" }.Concat(values.Select(value => value.Name)).ToArray());
                    break;
                }
                case "store_field":
                {
                    var target = LowerExpression(statement.Operands[0]);
                    var value = LowerExpression(statement.Operands[1]);
                    if (statement.Type != null) value = Coerce(value, statement.Type);
                    Emit(null, "none", "store.field", target.Name, statement.Target!, value.Name);
                    break;
                }
                case "return":
                {
                    var lowered = statement.Operands.Select(LowerExpression).ToList();
                    if (lowered.Count > 0) lowered[0] = Coerce(lowered[0], _returnType);
                    var values = lowered.Select(value => value.Name).ToList();
                    Terminate("return", values.Count == 0 ? ["none"] : values);
                    break;
                }
                case "throw":
                {
                    var value = LowerExpression(statement.Operands[0]);
                    Terminate("throw", [value.Name]);
                    break;
                }
                case "break":
                    if (_loops.Count == 0) Terminate("trap", ["break_outside_loop"]);
                    else Terminate("jump", targets: [_loops.Peek().Break]);
                    break;
                case "continue":
                    if (_loops.Count == 0) Terminate("trap", ["continue_outside_loop"]);
                    else Terminate("jump", targets: [_loops.Peek().Continue]);
                    break;
                case "if": LowerIf(statement); break;
                case "while": LowerWhile(statement); break;
                case "for": LowerFor(statement); break;
                case "try": LowerTry(statement); break;
                default:
                    Emit(null, "none", $"runtime.{statement.Op}", statement.Detail ?? "");
                    break;
            }
        }

        private void LowerIf(HirStatement statement)
        {
            var condition = LowerExpression(statement.Operands[0]);
            var thenBlock = NewBlock("if_then");
            var elseBlock = statement.Regions.Count > 1 ? NewBlock("if_else") : null;
            var mergeBlock = NewBlock("if_merge");
            Terminate("branch", [condition.Name],
                [thenBlock.Label, elseBlock?.Label ?? mergeBlock.Label]);

            _current = thenBlock;
            LowerBlock(statement.Regions[0]);
            JumpIfOpen(mergeBlock.Label);

            if (elseBlock != null)
            {
                _current = elseBlock;
                LowerBlock(statement.Regions[1]);
                JumpIfOpen(mergeBlock.Label);
            }
            _current = mergeBlock;
        }

        private void LowerWhile(HirStatement statement)
        {
            var header = NewBlock("while_header");
            var body = NewBlock("while_body");
            var exit = NewBlock("while_exit");
            JumpIfOpen(header.Label);

            _current = header;
            var condition = LowerExpression(statement.Operands[0]);
            Terminate("branch", [condition.Name], [body.Label, exit.Label]);

            _current = body;
            _loops.Push((exit.Label, header.Label));
            LowerBlock(statement.Regions[0]);
            _loops.Pop();
            JumpIfOpen(header.Label);
            _current = exit;
        }

        private void LowerFor(HirStatement statement)
        {
            var iterable = LowerExpression(statement.Operands[0]);
            var iterator = EmitValue("iterator", "iter.begin", iterable.Name);
            var header = NewBlock("for_header");
            var body = NewBlock("for_body");
            var exit = NewBlock("for_exit");
            JumpIfOpen(header.Label);

            _current = header;
            var hasNext = EmitValue("bool", "iter.has_next", iterator.Name);
            Terminate("branch", [hasNext.Name], [body.Label, exit.Label]);

            _current = body;
            var item = EmitValue(statement.Type ?? "dynamic", "iter.next", iterator.Name);
            Bind(statement.Target!, statement.Type ?? "dynamic", item);
            _loops.Push((exit.Label, header.Label));
            LowerBlock(statement.Regions[0]);
            _loops.Pop();
            JumpIfOpen(header.Label);
            _current = exit;
        }

        private void LowerTry(HirStatement statement)
        {
            if (statement.Regions.Count < 2)
            {
                Emit(null, "none", "try.unhandled");
                LowerBlock(statement.Regions[0]);
                return;
            }

            var tryBlock = NewBlock("try_body");
            var catchBlock = NewBlock("catch_body");
            var merge = NewBlock("try_merge");
            Emit(null, "none", "try.push", catchBlock.Label);
            JumpIfOpen(tryBlock.Label);

            _current = tryBlock;
            LowerBlock(statement.Regions[0]);
            if (_current.Terminator == null)
            {
                Emit(null, "none", "try.pop");
                Terminate("jump", targets: [merge.Label]);
            }

            _current = catchBlock;
            if (statement.Target != null)
            {
                var error = EmitValue("string", "exception.current");
                Bind(statement.Target, "string", error);
            }
            LowerBlock(statement.Regions[1]);
            JumpIfOpen(merge.Label);
            _current = merge;
        }

        private void Bind(string name, string type, MirValue value, bool isConst = false)
        {
            if (_locals.Add(name))
                Emit(null, type, isConst ? "alloc.const" : "alloc", $"%{name}");
            Emit(null, "none", "store", $"%{name}", value.Name);
        }

        public void Emit(string? result, string type, string op, params string[] arguments) =>
            _current.Instructions.Add(new MirInstruction(result, type, op, arguments.ToList()));

        private MirValue EmitValue(string type, string op, params string[] arguments)
        {
            var result = $"%t{_tempId++}";
            Emit(result, type, op, arguments);
            return new MirValue(result, type);
        }

        private MirBlock NewBlock(string purpose)
        {
            var block = new MirBlock($"bb{_blockId++}_{purpose}");
            _blocks.Add(block);
            return block;
        }

        private void JumpIfOpen(string target)
        {
            if (_current.Terminator == null) Terminate("jump", targets: [target]);
        }

        private void Terminate(string op, List<string>? arguments = null, List<string>? targets = null) =>
            _current.Terminator = new MirTerminator(op, arguments ?? [], targets ?? []);
    }
}

static class MirFormatter
{
    public static string Format(MirProgram program)
    {
        var output = new StringBuilder("mako.mir 1\n");
        foreach (var item in program.Structs)
        {
            output.Append("\nstruct ").Append(item.Name).AppendLine(" {");
            foreach (var field in item.Fields)
                output.Append("    field ").Append(field.Name).Append(": ").AppendLine(field.Type);
            output.AppendLine("}");
        }
        foreach (var fn in program.Functions)
        {
            output.Append("\nfn ").Append(fn.Name).Append('(')
                .Append(string.Join(", ", fn.Parameters.Select(p => $"%{p.Name}: {p.Type}")))
                .Append(") -> ").AppendLine(fn.ReturnType);
            foreach (var block in fn.Blocks)
            {
                output.Append(block.Label).AppendLine(":");
                foreach (var instruction in block.Instructions)
                {
                    output.Append("    ");
                    if (instruction.Result != null)
                        output.Append(instruction.Result).Append(": ").Append(instruction.Type).Append(" = ");
                    output.Append(instruction.Op);
                    if (instruction.Arguments.Count > 0)
                        output.Append(' ').Append(string.Join(", ", instruction.Arguments));
                    if (instruction.Result == null && instruction.Type != "none")
                        output.Append(": ").Append(instruction.Type);
                    output.AppendLine();
                }
                var term = block.Terminator ?? new MirTerminator("unreachable", [], []);
                output.Append("    ").Append(term.Op);
                if (term.Arguments.Count > 0) output.Append(' ').Append(string.Join(", ", term.Arguments));
                if (term.Targets.Count > 0) output.Append(" -> ").Append(string.Join(", ", term.Targets));
                output.AppendLine();
            }
        }
        return output.ToString();
    }
}
