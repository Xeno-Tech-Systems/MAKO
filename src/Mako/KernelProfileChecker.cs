namespace Mako;

/// Enforces the deliberately small MAKO subset that can be lowered toward a
/// freestanding kernel without the managed interpreter/runtime. This profile
/// will expand in lockstep with the native backend.
static class KernelProfileChecker
{
    private static readonly HashSet<string> KernelIntrinsics = new(StringComparer.Ordinal)
    {
        "volatile_load_u8", "volatile_load_u16", "volatile_load_u32", "volatile_load_u64",
        "volatile_store_u8", "volatile_store_u16", "volatile_store_u32", "volatile_store_u64",
        "vptr_from_u8", "vptr_from_u16", "vptr_from_u32", "vptr_from_u64",
        "vptr_offset_u8", "vptr_offset_u16", "vptr_offset_u32", "vptr_offset_u64",
        "vptr_read_u8", "vptr_read_u16", "vptr_read_u32", "vptr_read_u64",
        "vptr_write_u8", "vptr_write_u16", "vptr_write_u32", "vptr_write_u64",
        "abi_syscall0", "abi_syscall1", "abi_syscall2",
        "abi_syscall3", "abi_syscall4", "abi_syscall5",
    };
    private static readonly HashSet<string> ScalarTypes = new(StringComparer.Ordinal)
    {
        "i8", "i16", "i32", "i64", "isize",
        "u8", "u16", "u32", "u64", "usize",
        "bool", "none", "<integer literal>",
    };

    public static List<CheckIssue> Check(ProgramNode program, TypeAnalysis analysis)
    {
        var issues = new List<CheckIssue>();
        var seen = new HashSet<(int, string)>();
        void Add(int line, string message)
        {
            if (seen.Add((line, message))) issues.Add(new CheckIssue(line, $"kernel profile: {message}"));
        }

        if (program.Packages.Count > 0)
            Add(1, "native packages are unavailable in freestanding code");
        if (program.Imports.Count > 0)
            Add(1, "module imports are not native-linked yet");
        if (program.Constants.Count > 0)
            Add(1, "top-level constants need typed native storage support");
        foreach (var structure in program.Structs)
            Add(structure.Line, $"struct '{structure.Name}' needs native layout support");

        foreach (var function in program.Functions)
        {
            var signature = analysis.FunctionTypes[function];
            foreach (var parameter in function.Params)
            {
                if (!function.ParamTypes.ContainsKey(parameter))
                    Add(function.Line, $"parameter '{function.Name}.{parameter}' must have an explicit type");
                CheckType(signature.Parameters[parameter], function.Line,
                    $"parameter '{function.Name}.{parameter}'", Add);
            }
            if (function.ReturnType == null)
                Add(function.Line, $"function '{function.Name}' must declare a return type");
            CheckType(signature.ReturnType, function.Line, $"return type of '{function.Name}'", Add);

            var declared = function.Params.ToHashSet(StringComparer.Ordinal);
            CheckBlock(function.Body, declared, program, analysis, Add);
        }

        CheckBlock(program.Body, new HashSet<string>(StringComparer.Ordinal), program, analysis, Add);
        return issues;
    }

    private static void CheckType(string type, int line, string context,
        Action<int, string> add)
    {
        if (!ScalarTypes.Contains(type) && !IsVolatilePointer(type))
            add(line, $"{context} uses unsupported type '{Printable(type)}'");
    }

    private static bool IsVolatilePointer(string type) =>
        type is "vptr<u8>" or "vptr<u16>" or "vptr<u32>" or "vptr<u64>";

    private static void CheckBlock(List<Statement> statements, HashSet<string> declared,
        ProgramNode program, TypeAnalysis analysis, Action<int, string> add)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case AssignStmt assignment:
                    if (!declared.Contains(assignment.Name) && assignment.TypeHint == null)
                        add(statement.Line, $"first assignment to '{assignment.Name}' needs an explicit type");
                    declared.Add(assignment.Name);
                    if (analysis.StatementTypes.TryGetValue(statement, out var assignmentType))
                        CheckType(assignmentType, statement.Line, $"binding '{assignment.Name}'", add);
                    CheckExpression(assignment.Value, statement.Line, program, analysis, add);
                    break;

                case IfStmt conditional:
                    CheckExpression(conditional.Condition, statement.Line, program, analysis, add);
                    CheckBlock(conditional.Then,
                        new HashSet<string>(declared, StringComparer.Ordinal), program, analysis, add);
                    CheckBlock(conditional.Else,
                        new HashSet<string>(declared, StringComparer.Ordinal), program, analysis, add);
                    break;

                case WhileStmt loop:
                    CheckExpression(loop.Condition, statement.Line, program, analysis, add);
                    CheckBlock(loop.Body,
                        new HashSet<string>(declared, StringComparer.Ordinal), program, analysis, add);
                    break;

                case ReturnStmt { Value: not null } returned:
                    CheckExpression(returned.Value, statement.Line, program, analysis, add);
                    break;
                case ReturnStmt or BreakStmt or ContinueStmt:
                    break;

                case ExprStmt expression:
                    CheckExpression(expression.Value, statement.Line, program, analysis, add);
                    break;

                default:
                    add(statement.Line,
                        $"'{StatementName(statement)}' requires the managed runtime or unsupported native lowering");
                    break;
            }
        }
    }

    private static void CheckExpression(Expr expression, int fallbackLine, ProgramNode program,
        TypeAnalysis analysis, Action<int, string> add)
    {
        var line = expression.Line > 0 ? expression.Line : fallbackLine;
        if (analysis.ExpressionTypes.TryGetValue(expression, out var type))
            CheckType(type, line, "expression", add);

        switch (expression)
        {
            case NumberLit or BoolLit or NullLit or IdentExpr:
                return;
            case BinaryExpr binary:
                CheckExpression(binary.Left, line, program, analysis, add);
                CheckExpression(binary.Right, line, program, analysis, add);
                return;
            case LogicalExpr logical:
                CheckExpression(logical.Left, line, program, analysis, add);
                CheckExpression(logical.Right, line, program, analysis, add);
                return;
            case UnaryExpr unary:
                CheckExpression(unary.Operand, line, program, analysis, add);
                return;
            case CallExpr call:
                if (!program.Functions.Any(function => function.Name == call.Name) &&
                    !KernelIntrinsics.Contains(call.Name))
                    add(line, $"call to '{call.Name}' has no freestanding native symbol");
                if (KernelIntrinsics.Contains(call.Name))
                {
                    var expected = call.Name.StartsWith("abi_syscall", StringComparison.Ordinal)
                        ? call.Name[^1] - '0' + 1
                        : call.Name.StartsWith("volatile_load_", StringComparison.Ordinal) ||
                            call.Name.StartsWith("vptr_from_", StringComparison.Ordinal) ||
                            call.Name.StartsWith("vptr_read_", StringComparison.Ordinal) ? 1 : 2;
                    if (call.Args.Count != expected)
                        add(line, $"intrinsic '{call.Name}' expects {expected} argument(s)");
                }
                foreach (var argument in call.Args)
                    CheckExpression(argument, line, program, analysis, add);
                return;
            case NamespacedCallExpr call:
                var symbol = $"{call.Ns}.{call.Func}";
                if (!program.Functions.Any(function => function.Name == symbol))
                    add(line, $"call to '{symbol}' has no freestanding native symbol");
                foreach (var argument in call.Args)
                    CheckExpression(argument, line, program, analysis, add);
                return;
            default:
                add(line, $"'{ExpressionName(expression)}' is unavailable in the kernel profile");
                return;
        }
    }

    private static string Printable(string type) => type switch
    {
        "<integer literal>" => "integer literal",
        "<float literal>" => "floating-point literal",
        _ => type,
    };

    private static string StatementName(Statement statement) => statement switch
    {
        PrintStmt or PrintnlStmt => "print",
        IndexAssignStmt => "index assignment",
        FieldAssignStmt => "field assignment",
        ForStmt => "for loop",
        RunStmt => "run",
        ConstStmt => "const",
        TryStmt => "try/catch",
        ThrowStmt => "throw",
        _ => statement.GetType().Name,
    };

    private static string ExpressionName(Expr expression) => expression switch
    {
        StringLit or TemplateStringExpr => "string",
        ListLit => "list",
        DictLit => "dict",
        LambdaExpr => "lambda",
        IndexExpr => "indexing",
        FieldExpr => "field access",
        StructLitExpr => "struct construction",
        InputExpr => "input",
        NamespacedCallExpr or MethodCallExpr => "package or method call",
        _ => expression.GetType().Name,
    };
}
