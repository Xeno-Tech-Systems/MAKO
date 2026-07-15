namespace Mako;

/// Reusable semantic information produced by the systems checker. The HIR
/// lowerer and future native backends consume the same inferred types that
/// power `mko check`, avoiding a second, subtly different type system.
record TypeAnalysis(
    IReadOnlyList<CheckIssue> Issues,
    IReadOnlyDictionary<Expr, string> ExpressionTypes,
    IReadOnlyDictionary<Statement, string> StatementTypes,
    IReadOnlyDictionary<FnDecl, FunctionTypeInfo> FunctionTypes,
    IReadOnlyDictionary<StructDecl, StructTypeInfo> StructTypes);

record FunctionTypeInfo(IReadOnlyDictionary<string, string> Parameters, string ReturnType);
record StructTypeInfo(IReadOnlyDictionary<string, string> Fields);

/// Static checking for the opt-in systems subset of MAKO. Existing untyped
/// code continues to flow through as `dynamic`; adding an annotation makes
/// that boundary strict and checkable without changing interpreter semantics.
static class SystemsTypeChecker
{
    private const string Dynamic = "dynamic";
    private const string IntegerLiteral = "<integer literal>";
    private const string FloatLiteral = "<float literal>";

    private sealed record Binding(string Type, bool IsTyped);

    private static readonly HashSet<string> PrimitiveTypes = new(StringComparer.Ordinal)
    {
        Dynamic, "number", "i8", "i16", "i32", "i64", "isize",
        "u8", "u16", "u32", "u64", "usize", "f32", "f64",
        "bool", "string", "none", "list", "dict", "fn",
    };

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["any"] = Dynamic,
        ["int"] = "i64",
        ["float"] = "f64",
        ["double"] = "f64",
        ["str"] = "string",
        ["boolean"] = "bool",
        ["array"] = "list",
        ["map"] = "dict",
        ["object"] = "dict",
        ["null"] = "none",
        ["void"] = "none",
    };

    private sealed class Context(ProgramNode program, List<CheckIssue> issues)
    {
        private readonly HashSet<(int Line, string Message)> _reported = [];
        private readonly Dictionary<Expr, string> _expressionTypes =
            new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<Statement, string> _statementTypes =
            new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<FnDecl, FunctionTypeInfo> _functionTypes =
            new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<StructDecl, StructTypeInfo> _structTypes =
            new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, FnDecl> _functions =
            program.Functions.ToDictionary(fn => fn.Name, StringComparer.Ordinal);
        private readonly Dictionary<string, StructDecl> _structs =
            program.Structs.ToDictionary(s => s.Name, StringComparer.Ordinal);

        public IReadOnlyDictionary<Expr, string> ExpressionTypes => _expressionTypes;
        public IReadOnlyDictionary<Statement, string> StatementTypes => _statementTypes;
        public IReadOnlyDictionary<FnDecl, FunctionTypeInfo> FunctionTypes => _functionTypes;
        public IReadOnlyDictionary<StructDecl, StructTypeInfo> StructTypes => _structTypes;

        public void Run()
        {
            ValidateDeclarations();

            var globals = new Dictionary<string, Binding>(StringComparer.Ordinal);
            foreach (var (name, value) in program.Constants)
                globals[name] = new Binding(Infer(value, globals), true);

            CheckBlock(program.Body, globals, null);
            foreach (var fn in program.Functions)
            {
                var env = new Dictionary<string, Binding>(globals, StringComparer.Ordinal);
                foreach (var param in fn.Params)
                {
                    var typed = fn.ParamTypes.TryGetValue(param, out var annotation);
                    env[param] = new Binding(_functionTypes[fn].Parameters[param], typed);
                }
                CheckBlock(fn.Body, env, fn);

                var returnType = ResolveOptional(fn.ReturnType, fn.Line);
                if (returnType is not null and not "none" and not Dynamic && !AlwaysReturns(fn.Body))
                    Add(fn.Line, $"function '{fn.Name}' promises '{fn.ReturnType}' but not every path returns a value");
            }
        }

        private void ValidateDeclarations()
        {
            foreach (var fn in program.Functions)
            {
                var parameters = fn.Params.ToDictionary(
                    param => param,
                    param => fn.ParamTypes.TryGetValue(param, out var type)
                        ? Resolve(type, fn.Line)
                        : Dynamic,
                    StringComparer.Ordinal);
                var returnType = ResolveOptional(fn.ReturnType, fn.Line) ?? Dynamic;
                _functionTypes[fn] = new FunctionTypeInfo(parameters, returnType);
            }
            foreach (var decl in program.Structs)
            {
                var fields = decl.Fields.ToDictionary(
                    field => field,
                    field => decl.FieldTypes.TryGetValue(field, out var type)
                        ? Resolve(type, decl.Line)
                        : Dynamic,
                    StringComparer.Ordinal);
                _structTypes[decl] = new StructTypeInfo(fields);
            }
        }

        private void CheckBlock(List<Statement> body, Dictionary<string, Binding> env, FnDecl? function)
        {
            foreach (var stmt in body)
            {
                switch (stmt)
                {
                    case AssignStmt a:
                    {
                        var actual = Infer(a.Value, env);
                        if (a.TypeHint != null)
                        {
                            var expected = Resolve(a.TypeHint, a.Line);
                            CheckAssignable(expected, actual, a.Value, env, a.Line,
                                $"cannot initialize '{a.Name}: {a.TypeHint}' with {Display(actual)}");
                            env[a.Name] = new Binding(expected, true);
                            _statementTypes[a] = expected;
                        }
                        else if (env.TryGetValue(a.Name, out var prior) && prior.IsTyped)
                        {
                            CheckAssignable(prior.Type, actual, a.Value, env, a.Line,
                                $"cannot assign {Display(actual)} to typed variable '{a.Name}: {prior.Type}'");
                            _statementTypes[a] = prior.Type;
                        }
                        else
                        {
                            env[a.Name] = new Binding(actual, false);
                            _statementTypes[a] = actual;
                        }
                        break;
                    }
                    case ConstStmt c:
                        var constType = Infer(c.Value, env);
                        env[c.Name] = new Binding(constType, true);
                        _statementTypes[c] = constType;
                        break;
                    case ReturnStmt r:
                        CheckReturn(r, function, env);
                        break;
                    case PrintStmt p: Infer(p.Value, env); break;
                    case PrintnlStmt p: Infer(p.Value, env); break;
                    case ExprStmt e: Infer(e.Value, env); break;
                    case ThrowStmt t: Infer(t.Message, env); break;
                    case RunStmt r: Infer(r.Command, env); break;
                    case IndexAssignStmt ia:
                        CheckIndexAssignment(ia, env);
                        break;
                    case FieldAssignStmt fa:
                        CheckFieldAssignment(fa, env);
                        break;
                    case IfStmt i:
                        Infer(i.Condition, env);
                        CheckBlock(i.Then, new Dictionary<string, Binding>(env, StringComparer.Ordinal), function);
                        CheckBlock(i.Else, new Dictionary<string, Binding>(env, StringComparer.Ordinal), function);
                        break;
                    case WhileStmt w:
                        Infer(w.Condition, env);
                        CheckBlock(w.Body, new Dictionary<string, Binding>(env, StringComparer.Ordinal), function);
                        break;
                    case ForStmt f:
                    {
                        var iterable = Infer(f.Iterable, env);
                        var itemType = Dynamic;
                        if (TryGeneric(iterable, out var container, out var args))
                            itemType = container == "list" ? args[0]
                                : container == "dict" ? args[0]
                                : Dynamic;
                        var loopEnv = new Dictionary<string, Binding>(env, StringComparer.Ordinal)
                        {
                            [f.Var] = new Binding(itemType, itemType != Dynamic),
                        };
                        _statementTypes[f] = itemType;
                        CheckBlock(f.Body, loopEnv, function);
                        break;
                    }
                    case TryStmt t:
                        CheckBlock(t.Try, new Dictionary<string, Binding>(env, StringComparer.Ordinal), function);
                        var catchEnv = new Dictionary<string, Binding>(env, StringComparer.Ordinal);
                        if (t.CatchVar != null) catchEnv[t.CatchVar] = new Binding("string", true);
                        CheckBlock(t.Catch, catchEnv, function);
                        break;
                }
            }
        }

        private void CheckReturn(ReturnStmt stmt, FnDecl? function, Dictionary<string, Binding> env)
        {
            if (function?.ReturnType == null)
            {
                if (stmt.Value != null) Infer(stmt.Value, env);
                return;
            }

            var expected = Resolve(function.ReturnType, stmt.Line);
            var actual = stmt.Value == null ? "none" : Infer(stmt.Value, env);
            CheckAssignable(expected, actual, stmt.Value, env, stmt.Line,
                $"function '{function.Name}' returns {Display(actual)}, expected '{function.ReturnType}'");
        }

        private void CheckFieldAssignment(FieldAssignStmt stmt, Dictionary<string, Binding> env)
        {
            var target = Infer(stmt.Target, env);
            var value = Infer(stmt.Value, env);
            if (!_structs.TryGetValue(target, out var decl) ||
                !decl.FieldTypes.TryGetValue(stmt.Field, out var annotation)) return;
            var expected = Resolve(annotation, stmt.Line);
            _statementTypes[stmt] = expected;
            CheckAssignable(expected, value, stmt.Value, env, stmt.Line,
                $"cannot assign {Display(value)} to field '{target}.{stmt.Field}: {annotation}'");
        }

        private void CheckIndexAssignment(IndexAssignStmt stmt, Dictionary<string, Binding> env)
        {
            var current = env.TryGetValue(stmt.Name, out var binding) ? binding.Type : Dynamic;
            foreach (var index in stmt.Indices)
            {
                var indexType = Infer(index, env);
                if (!TryGeneric(current, out var container, out var args))
                {
                    current = Dynamic;
                    continue;
                }

                if (container == "list")
                {
                    if (!IsNumeric(indexType) && indexType != Dynamic)
                        Add(index.Line > 0 ? index.Line : stmt.Line,
                            $"list index must be numeric, got {Display(indexType)}");
                    current = args[0];
                }
                else if (container == "dict")
                {
                    CheckAssignable(args[0], indexType, index, env,
                        index.Line > 0 ? index.Line : stmt.Line,
                        $"dict index expects '{args[0]}', got {Display(indexType)}");
                    current = args[1];
                }
                else current = Dynamic;
            }

            var valueType = Infer(stmt.Value, env);
            _statementTypes[stmt] = current;
            CheckAssignable(current, valueType, stmt.Value, env, stmt.Line,
                $"indexed value of '{stmt.Name}' expects '{current}', got {Display(valueType)}");
        }

        private string Infer(Expr expr, Dictionary<string, Binding> env)
        {
            var type = InferCore(expr, env);
            _expressionTypes[expr] = type;
            return type;
        }

        private string InferCore(Expr expr, Dictionary<string, Binding> env) => expr switch
        {
            StringLit or TemplateStringExpr => "string",
            NumberLit n => Math.Truncate(n.Value) == n.Value ? IntegerLiteral : FloatLiteral,
            BoolLit => "bool",
            NullLit => "none",
            ListLit => "list",
            DictLit => "dict",
            LambdaExpr => "fn",
            IdentExpr id => InferIdentifier(id, env),
            StructLitExpr s => CheckStructLiteral(s, env),
            FieldExpr f => InferField(f, env),
            BinaryExpr b => InferBinary(b, env),
            LogicalExpr l => InferLogical(l, env),
            UnaryExpr u => InferUnary(u, env),
            CallExpr c => InferCall(c, env),
            NamespacedCallExpr c => InferNamespacedCall(c, env),
            MethodCallExpr m => InferMethod(m, env),
            IndexExpr i => InferIndex(i, env),
            InputExpr i => InferInput(i, env),
            _ => Dynamic,
        };

        private string CheckStructLiteral(StructLitExpr literal, Dictionary<string, Binding> env)
        {
            if (!_structs.TryGetValue(literal.TypeName, out var decl)) return Dynamic;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (field, value) in literal.Fields)
            {
                var actual = Infer(value, env);
                if (!decl.Fields.Contains(field))
                {
                    Add(literal.Line, $"struct '{decl.Name}' has no field '{field}'");
                    continue;
                }
                if (!seen.Add(field))
                {
                    Add(literal.Line, $"field '{decl.Name}.{field}' is initialized more than once");
                    continue;
                }
                if (!decl.FieldTypes.TryGetValue(field, out var annotation)) continue;
                var expected = Resolve(annotation, literal.Line);
                CheckAssignable(expected, actual, value, env, value.Line > 0 ? value.Line : literal.Line,
                    $"field '{decl.Name}.{field}' expects '{annotation}', got {Display(actual)}");
            }
            foreach (var missing in decl.Fields.Where(field => !seen.Contains(field)))
                Add(literal.Line, $"struct '{decl.Name}' is missing field '{missing}'");
            return literal.TypeName;
        }

        private string InferIdentifier(IdentExpr identifier, Dictionary<string, Binding> env)
        {
            if (env.TryGetValue(identifier.Name, out var direct)) return direct.Type;

            // The parser keeps a leading `value.field` in a compact identifier
            // form because the same syntax is also used for package constants.
            // Resolve it as a typed struct field when the left side is a local.
            var dot = identifier.Name.IndexOf('.');
            if (dot <= 0) return Dynamic;
            var targetName = identifier.Name[..dot];
            var fieldName = identifier.Name[(dot + 1)..];
            if (!env.TryGetValue(targetName, out var target) ||
                !_structs.TryGetValue(target.Type, out var decl) ||
                !decl.FieldTypes.TryGetValue(fieldName, out var annotation)) return Dynamic;
            return Resolve(annotation, identifier.Line);
        }

        private string InferField(FieldExpr field, Dictionary<string, Binding> env)
        {
            var target = Infer(field.Target, env);
            if (_structs.TryGetValue(target, out var decl) &&
                decl.FieldTypes.TryGetValue(field.Field, out var annotation))
                return Resolve(annotation, field.Line);
            return Dynamic;
        }

        private string InferBinary(BinaryExpr binary, Dictionary<string, Binding> env)
        {
            var left = Infer(binary.Left, env);
            var right = Infer(binary.Right, env);
            var comparison = binary.Op is "==" or "!=" or "<" or ">" or "<=" or ">=";

            if ((IsInteger(left) || left is "f32" or "f64") &&
                right is IntegerLiteral or FloatLiteral)
            {
                CheckAssignable(left, right, binary.Right, env, binary.Line,
                    $"literal operand does not fit '{left}'");
                return comparison ? "bool" : left;
            }
            if ((IsInteger(right) || right is "f32" or "f64") &&
                left is IntegerLiteral or FloatLiteral)
            {
                CheckAssignable(right, left, binary.Left, env, binary.Line,
                    $"literal operand does not fit '{right}'");
                return comparison ? "bool" : right;
            }

            if (comparison) return "bool";
            if (binary.Op == "+" && (left == "string" || right == "string")) return "string";
            if (left == right) return left;
            if (IsNumeric(left) && IsNumeric(right))
                return left == FloatLiteral || right == FloatLiteral ? FloatLiteral : "number";
            return Dynamic;
        }

        private string InferLogical(LogicalExpr logical, Dictionary<string, Binding> env)
        {
            Infer(logical.Left, env);
            Infer(logical.Right, env);
            return "bool";
        }

        private string InferUnary(UnaryExpr unary, Dictionary<string, Binding> env)
        {
            var operand = Infer(unary.Operand, env);
            return unary.Op == "!" ? "bool" : operand;
        }

        private string InferCall(CallExpr call, Dictionary<string, Binding> env)
        {
            if (!_functions.TryGetValue(call.Name, out var fn))
            {
                var actualTypes = call.Args.Select(arg => Infer(arg, env)).ToList();
                CheckKernelIntrinsic(call, actualTypes);
                return BuiltinReturnType(call.Name);
            }

            if (call.Args.Count != fn.Params.Count)
                Add(call.Line, $"function '{fn.Name}' expects {fn.Params.Count} argument(s), got {call.Args.Count}");

            for (var i = 0; i < call.Args.Count; i++)
            {
                var actual = Infer(call.Args[i], env);
                if (i >= fn.Params.Count) continue;
                var param = fn.Params[i];
                if (!fn.ParamTypes.TryGetValue(param, out var annotation)) continue;
                var expected = Resolve(annotation, call.Line);
                CheckAssignable(expected, actual, call.Args[i], env, call.Args[i].Line > 0 ? call.Args[i].Line : call.Line,
                    $"argument '{param}' of '{fn.Name}' expects '{annotation}', got {Display(actual)}");
            }
            return ResolveOptional(fn.ReturnType, fn.Line) ?? Dynamic;
        }

        private void CheckKernelIntrinsic(CallExpr call, List<string> actualTypes)
        {
            if (call.Name.StartsWith("abi_syscall", StringComparison.Ordinal))
            {
                var syscallArgumentCount = call.Name[^1] - '0' + 1;
                if (actualTypes.Count != syscallArgumentCount)
                    Add(call.Line, $"intrinsic '{call.Name}' expects {syscallArgumentCount} argument(s), got {actualTypes.Count}");
                for (var i = 0; i < actualTypes.Count; i++)
                    if (!IsInteger(actualTypes[i]) && actualTypes[i] != IntegerLiteral)
                        Add(call.Line, $"intrinsic '{call.Name}' expects integer arguments");
                return;
            }
            if (!call.Name.StartsWith("vptr_", StringComparison.Ordinal)) return;
            var widthMarker = call.Name[(call.Name.LastIndexOf('_') + 1)..];
            var pointerType = $"vptr<{widthMarker}>";
            var expectedCount = call.Name.StartsWith("vptr_from_", StringComparison.Ordinal) ||
                call.Name.StartsWith("vptr_read_", StringComparison.Ordinal) ? 1 : 2;
            if (actualTypes.Count != expectedCount)
                Add(call.Line, $"intrinsic '{call.Name}' expects {expectedCount} argument(s), got {actualTypes.Count}");
            if (actualTypes.Count == 0) return;
            if (call.Name.StartsWith("vptr_from_", StringComparison.Ordinal))
            {
                if (!IsInteger(actualTypes[0]) && actualTypes[0] != IntegerLiteral)
                    Add(call.Line, $"intrinsic '{call.Name}' expects an integer address");
                return;
            }
            if (actualTypes[0] != pointerType)
                Add(call.Line, $"intrinsic '{call.Name}' expects '{pointerType}', got {Display(actualTypes[0])}");
            if (call.Name.StartsWith("vptr_offset_", StringComparison.Ordinal) && actualTypes.Count > 1 &&
                !IsInteger(actualTypes[1]) && actualTypes[1] != IntegerLiteral)
                Add(call.Line, $"intrinsic '{call.Name}' expects an integer element offset");
        }

        private string InferNamespacedCall(NamespacedCallExpr call, Dictionary<string, Binding> env)
        {
            if (_functions.TryGetValue($"{call.Ns}.{call.Func}", out var namespaceFunction))
            {
                if (call.Args.Count != namespaceFunction.Params.Count)
                    Add(call.Line, $"function '{namespaceFunction.Name}' expects {namespaceFunction.Params.Count} argument(s), got {call.Args.Count}");
                for (var i = 0; i < call.Args.Count; i++)
                {
                    var actual = Infer(call.Args[i], env);
                    if (i >= namespaceFunction.Params.Count) continue;
                    var parameter = namespaceFunction.Params[i];
                    if (!namespaceFunction.ParamTypes.TryGetValue(parameter, out var annotation)) continue;
                    var expected = Resolve(annotation, call.Line);
                    CheckAssignable(expected, actual, call.Args[i], env, call.Line,
                        $"argument '{parameter}' of '{namespaceFunction.Name}' expects '{annotation}', got {Display(actual)}");
                }
                return ResolveOptional(namespaceFunction.ReturnType, namespaceFunction.Line) ?? Dynamic;
            }
            if (!env.TryGetValue(call.Ns, out var target) ||
                !_functions.TryGetValue($"{target.Type}.{call.Func}", out var fn))
                return InferArgs(call.Args, env);

            var expectedCount = Math.Max(0, fn.Params.Count - 1);
            if (call.Args.Count != expectedCount)
                Add(call.Line, $"method '{fn.Name}' expects {expectedCount} argument(s), got {call.Args.Count}");

            // Struct methods reserve parameter zero for `self`; source call
            // arguments begin at parameter one.
            for (var i = 0; i < call.Args.Count; i++)
            {
                var actual = Infer(call.Args[i], env);
                var paramIndex = i + 1;
                if (paramIndex >= fn.Params.Count) continue;
                var param = fn.Params[paramIndex];
                if (!fn.ParamTypes.TryGetValue(param, out var annotation)) continue;
                var expected = Resolve(annotation, call.Line);
                CheckAssignable(expected, actual, call.Args[i], env, call.Line,
                    $"argument '{param}' of '{fn.Name}' expects '{annotation}', got {Display(actual)}");
            }
            return ResolveOptional(fn.ReturnType, fn.Line) ?? Dynamic;
        }

        private string InferMethod(MethodCallExpr call, Dictionary<string, Binding> env)
        {
            var target = Infer(call.Target, env);
            if (!_functions.TryGetValue($"{target}.{call.Method}", out var fn)) return Dynamic;
            var expectedCount = Math.Max(0, fn.Params.Count - 1);
            if (call.Args.Count != expectedCount)
                Add(call.Line, $"method '{fn.Name}' expects {expectedCount} argument(s), got {call.Args.Count}");
            for (var i = 0; i < call.Args.Count; i++)
            {
                var actual = Infer(call.Args[i], env);
                var paramIndex = i + 1;
                if (paramIndex >= fn.Params.Count) continue;
                var param = fn.Params[paramIndex];
                if (!fn.ParamTypes.TryGetValue(param, out var annotation)) continue;
                var expected = Resolve(annotation, call.Line);
                CheckAssignable(expected, actual, call.Args[i], env, call.Line,
                    $"argument '{param}' of '{fn.Name}' expects '{annotation}', got {Display(actual)}");
            }
            return ResolveOptional(fn.ReturnType, fn.Line) ?? Dynamic;
        }

        private string InferIndex(IndexExpr index, Dictionary<string, Binding> env)
        {
            var target = Infer(index.Target, env);
            var indexType = Infer(index.Index, env);
            if (TryGeneric(target, out var container, out var args))
            {
                if (container == "list")
                {
                    if (!IsNumeric(indexType) && indexType != Dynamic)
                        Add(index.Line, $"list index must be numeric, got {Display(indexType)}");
                    return args[0];
                }
                if (container == "dict")
                {
                    CheckAssignable(args[0], indexType, index.Index, env, index.Line,
                        $"dict index expects '{args[0]}', got {Display(indexType)}");
                    return args[1];
                }
            }
            if (target == "string") return "string";
            return Dynamic;
        }

        private string InferInput(InputExpr input, Dictionary<string, Binding> env)
        {
            Infer(input.Prompt, env);
            return "string";
        }

        private string InferArgs(List<Expr> args, Dictionary<string, Binding> env)
        {
            foreach (var arg in args) Infer(arg, env);
            return Dynamic;
        }

        private static string BuiltinReturnType(string name) => name switch
        {
            "volatile_load_u8" => "u8",
            "volatile_load_u16" => "u16",
            "volatile_load_u32" => "u32",
            "volatile_load_u64" => "u64",
            "volatile_store_u8" or "volatile_store_u16" or
                "volatile_store_u32" or "volatile_store_u64" => "none",
            "vptr_from_u8" or "vptr_offset_u8" => "vptr<u8>",
            "vptr_from_u16" or "vptr_offset_u16" => "vptr<u16>",
            "vptr_from_u32" or "vptr_offset_u32" => "vptr<u32>",
            "vptr_from_u64" or "vptr_offset_u64" => "vptr<u64>",
            "vptr_read_u8" => "u8",
            "vptr_read_u16" => "u16",
            "vptr_read_u32" => "u32",
            "vptr_read_u64" => "u64",
            "vptr_write_u8" or "vptr_write_u16" or
                "vptr_write_u32" or "vptr_write_u64" => "none",
            "abi_syscall0" or "abi_syscall1" or "abi_syscall2" or
                "abi_syscall3" or "abi_syscall4" or "abi_syscall5" => "u64",
            "len" or "to_num" or "abs" or "floor" or "ceil" or "round" or "sqrt" or "pow" or "min" or "max" => "number",
            "to_str" or "type" or "upper" or "lower" or "trim" or "replace" or "join" => "string",
            "contains" or "starts_with" or "ends_with" or "has" or "any" or "all" or "exists" => "bool",
            "range" => "list<number>",
            "split" or "keys" => "list<string>",
            "values" => "list<dynamic>",
            _ => Dynamic,
        };

        private string Resolve(string annotation, int line)
        {
            if (TryGeneric(annotation, out var genericName, out var genericArgs))
            {
                genericName = Aliases.GetValueOrDefault(genericName, genericName);
                var expectedArity = genericName switch
                {
                    "list" => 1,
                    "dict" => 2,
                    "vptr" => 1,
                    _ => 0,
                };
                if (expectedArity == 0)
                {
                    Add(line, $"type '{genericName}' cannot have type arguments");
                    return Dynamic;
                }
                if (genericArgs.Count != expectedArity)
                {
                    Add(line, $"type '{genericName}' expects {expectedArity} type argument{(expectedArity == 1 ? "" : "s")}, got {genericArgs.Count}");
                    return Dynamic;
                }
                var resolvedArgs = genericArgs.Select(arg => Resolve(arg, line));
                return $"{genericName}<{string.Join(", ", resolvedArgs)}>";
            }

            var resolved = Aliases.GetValueOrDefault(annotation, annotation);
            if (PrimitiveTypes.Contains(resolved) || _structs.ContainsKey(resolved)) return resolved;
            Add(line, $"unknown type '{annotation}'");
            return Dynamic;
        }

        private string? ResolveOptional(string? annotation, int line) =>
            annotation == null ? null : Resolve(annotation, line);

        private void CheckAssignable(string expected, string actual, Expr? value,
            Dictionary<string, Binding> env, int line, string message)
        {
            if (Assignable(expected, actual, value, env)) return;
            Add(line, message);
        }

        private bool Assignable(string expected, string actual, Expr? value,
            Dictionary<string, Binding> env)
        {
            if (expected == Dynamic || actual == Dynamic || expected == actual) return true;

            if (TryGeneric(expected, out var expectedName, out var expectedArgs))
            {
                if (expectedName == "list" && value is ListLit list)
                    return list.Items.All(item =>
                        Assignable(expectedArgs[0], Infer(item, env), item, env));
                if (expectedName == "dict" && value is DictLit dict)
                    return dict.Entries.All(entry =>
                        Assignable(expectedArgs[0], Infer(entry.Key, env), entry.Key, env) &&
                        Assignable(expectedArgs[1], Infer(entry.Value, env), entry.Value, env));

                if (!TryGeneric(actual, out var actualName, out var actualArgs) ||
                    expectedName != actualName || expectedArgs.Count != actualArgs.Count)
                    return false;

                // Mutable collections are invariant. This prevents a
                // list<u8> from being treated as list<number> and then having
                // an out-of-range number inserted through the wider view.
                return expectedArgs.SequenceEqual(actualArgs, StringComparer.Ordinal);
            }

            // An unparameterized collection is the dynamic form and accepts a
            // parameterized collection when code intentionally asks for it.
            if (expected is "list" or "dict" &&
                TryGeneric(actual, out var actualContainer, out _) && actualContainer == expected)
                return true;
            if (expected == "number" && IsNumeric(actual)) return true;
            if (expected is "f32" or "f64" && IsNumeric(actual)) return true;
            if (IsInteger(expected) && actual == IntegerLiteral)
                return TryConstantNumber(value, out var number) && FitsInteger(expected, number);
            return false;
        }

        private static bool TryGeneric(string type, out string name, out List<string> args)
        {
            name = "";
            args = [];
            var open = type.IndexOf('<');
            if (open <= 0 || !type.EndsWith('>')) return false;

            name = type[..open].Trim();
            var inner = type[(open + 1)..^1];
            var depth = 0;
            var start = 0;
            for (var i = 0; i < inner.Length; i++)
            {
                if (inner[i] == '<') depth++;
                else if (inner[i] == '>') depth--;
                else if (inner[i] == ',' && depth == 0)
                {
                    args.Add(inner[start..i].Trim());
                    start = i + 1;
                }
            }
            args.Add(inner[start..].Trim());
            return args.All(arg => arg.Length > 0);
        }

        private static bool IsNumeric(string type) =>
            type is "number" or "f32" or "f64" or IntegerLiteral or FloatLiteral || IsInteger(type);

        private static bool IsInteger(string type) => type is
            "i8" or "i16" or "i32" or "i64" or "isize" or
            "u8" or "u16" or "u32" or "u64" or "usize";

        private static bool TryConstantNumber(Expr? expr, out double value)
        {
            if (expr is NumberLit n) { value = n.Value; return true; }
            if (expr is UnaryExpr { Op: "-", Operand: NumberLit n2 }) { value = -n2.Value; return true; }
            value = 0;
            return false;
        }

        private static bool FitsInteger(string type, double value)
        {
            if (Math.Truncate(value) != value) return false;
            return type switch
            {
                "i8" => value is >= sbyte.MinValue and <= sbyte.MaxValue,
                "u8" => value is >= byte.MinValue and <= byte.MaxValue,
                "i16" => value is >= short.MinValue and <= short.MaxValue,
                "u16" => value is >= ushort.MinValue and <= ushort.MaxValue,
                "i32" => value is >= int.MinValue and <= int.MaxValue,
                "u32" => value is >= uint.MinValue and <= uint.MaxValue,
                "i64" or "isize" => value >= long.MinValue && value <= long.MaxValue,
                "u64" or "usize" => value >= 0 && value <= ulong.MaxValue,
                _ => false,
            };
        }

        private static bool AlwaysReturns(List<Statement> body)
        {
            foreach (var stmt in body)
            {
                if (stmt is ReturnStmt) return true;
                if (stmt is IfStmt i && i.Else.Count > 0 && AlwaysReturns(i.Then) && AlwaysReturns(i.Else))
                    return true;
                if (stmt is TryStmt t && t.HasCatch && AlwaysReturns(t.Try) && AlwaysReturns(t.Catch))
                    return true;
            }
            return false;
        }

        private static string Display(string type) => type switch
        {
            IntegerLiteral => "an integer literal",
            FloatLiteral => "a floating-point literal",
            _ => $"'{type}'",
        };

        private void Add(int line, string message)
        {
            if (_reported.Add((line, message))) issues.Add(new CheckIssue(line, message));
        }
    }

    public static List<CheckIssue> Check(ProgramNode program)
    {
        var issues = new List<CheckIssue>();
        Check(program, issues);
        return issues;
    }

    public static void Check(ProgramNode program, List<CheckIssue> issues) =>
        new Context(program, issues).Run();

    public static TypeAnalysis Analyze(ProgramNode program)
    {
        var issues = new List<CheckIssue>();
        var context = new Context(program, issues);
        context.Run();
        return new TypeAnalysis(issues, context.ExpressionTypes, context.StatementTypes,
            context.FunctionTypes, context.StructTypes);
    }
}
