using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Mako;

/// MAKO's first compiler-facing representation. HIR stays structured (if/while
/// regions have not yet become basic blocks) but every expression and binding
/// carries the semantic type produced by SystemsTypeChecker.
record HirProgram(
    List<HirGlobal> Globals,
    List<HirStruct> Structs,
    List<HirFunction> Functions,
    HirBlock Main);

record HirGlobal(string Name, string Type, HirExpression Initializer);
record HirStruct(string Name, List<HirField> Fields);
record HirField(string Name, string Type);
record HirFunction(string Name, List<HirParameter> Parameters, string ReturnType, HirBlock Body);
record HirParameter(string Name, string Type);
record HirBlock(List<HirStatement> Statements);

record HirStatement(
    string Op,
    string? Target,
    string? Type,
    List<HirExpression> Operands,
    List<HirBlock> Regions,
    int Line,
    string? Detail = null);

record HirExpression(
    string Op,
    string Type,
    string? Value,
    List<HirArgument> Arguments,
    HirBlock? Body,
    int Line);

record HirArgument(string? Label, HirExpression Value);

static class TypedHirLowerer
{
    public static HirProgram Lower(ProgramNode program, TypeAnalysis analysis)
    {
        if (analysis.Issues.Count > 0)
            throw new InvalidOperationException("cannot lower a program with static type errors");

        var globals = program.Constants.Select(item => new HirGlobal(
            item.Name,
            ExpressionType(item.Value, analysis),
            LowerExpression(item.Value, analysis))).ToList();

        var structs = program.Structs.Select(decl => new HirStruct(
            decl.Name,
            decl.Fields.Select(field => new HirField(
                field,
                analysis.StructTypes[decl].Fields[field])).ToList())).ToList();

        var functions = program.Functions.Select(fn => new HirFunction(
            fn.Name,
            fn.Params.Select(param => new HirParameter(
                param,
                analysis.FunctionTypes[fn].Parameters[param])).ToList(),
            analysis.FunctionTypes[fn].ReturnType,
            LowerBlock(fn.Body, analysis))).ToList();

        return new HirProgram(globals, structs, functions, LowerBlock(program.Body, analysis));
    }

    private static HirBlock LowerBlock(List<Statement> statements, TypeAnalysis analysis) =>
        new(statements.Select(stmt => LowerStatement(stmt, analysis)).ToList());

    private static HirStatement LowerStatement(Statement stmt, TypeAnalysis analysis)
    {
        List<HirExpression> Exprs(params Expr[] values) =>
            values.Select(value => LowerExpression(value, analysis)).ToList();
        List<HirBlock> Blocks(params List<Statement>[] values) =>
            values.Select(value => LowerBlock(value, analysis)).ToList();

        return stmt switch
        {
            PrintStmt p => Node("print", operands: Exprs(p.Value)),
            PrintnlStmt p => Node("print_no_line", operands: Exprs(p.Value)),
            AssignStmt a => Node("bind", a.Name, StatementType(a, analysis), Exprs(a.Value)),
            ConstStmt c => Node("const_bind", c.Name, StatementType(c, analysis), Exprs(c.Value)),
            IndexAssignStmt i => Node("store_index", i.Name, StatementType(i, analysis),
                i.Indices.Select(index => LowerExpression(index, analysis))
                    .Append(LowerExpression(i.Value, analysis)).ToList(),
                detail: i.Indices.Count.ToString(CultureInfo.InvariantCulture)),
            FieldAssignStmt f => Node("store_field", f.Field, StatementType(f, analysis), Exprs(f.Target, f.Value)),
            IfStmt i => Node("if", operands: Exprs(i.Condition),
                regions: i.Else.Count == 0 ? Blocks(i.Then) : Blocks(i.Then, i.Else)),
            WhileStmt w => Node("while", operands: Exprs(w.Condition), regions: Blocks(w.Body)),
            ForStmt f => Node("for", f.Var, StatementType(f, analysis), Exprs(f.Iterable), Blocks(f.Body)),
            BreakStmt => Node("break"),
            ContinueStmt => Node("continue"),
            ReturnStmt { Value: null } => Node("return"),
            ReturnStmt r => Node("return", operands: Exprs(r.Value!)),
            RunStmt r => Node("run", operands: Exprs(r.Command)),
            TryStmt t => Node("try", t.CatchVar, t.CatchVar == null ? null : "string",
                regions: t.HasCatch ? Blocks(t.Try, t.Catch) : Blocks(t.Try),
                detail: t.HasCatch ? "catch" : "no_catch"),
            ThrowStmt t => Node("throw", operands: Exprs(t.Message)),
            ExprStmt e => Node("eval", operands: Exprs(e.Value)),
            _ => Node("unknown", detail: stmt.GetType().Name),
        };

        HirStatement Node(string op, string? target = null, string? type = null,
            List<HirExpression>? operands = null, List<HirBlock>? regions = null,
            string? detail = null) =>
            new(op, target, type, operands ?? [], regions ?? [], stmt.Line, detail);
    }

    private static HirExpression LowerExpression(Expr expr, TypeAnalysis analysis)
    {
        string type = ExpressionType(expr, analysis);
        List<HirArgument> Args(params Expr[] values) =>
            values.Select(value => new HirArgument(null, LowerExpression(value, analysis))).ToList();

        return expr switch
        {
            StringLit s => Node("literal", JsonSerializer.Serialize(s.Value)),
            TemplateStringExpr t => Node("template", JsonSerializer.Serialize(t.Raw)),
            NumberLit n => Node("literal", n.Value.ToString("R", CultureInfo.InvariantCulture)),
            BoolLit b => Node("literal", b.Value ? "true" : "false"),
            NullLit => Node("literal", "none"),
            IdentExpr i => Node("load", i.Name),
            ListLit l => Node("list", args: Args(l.Items.ToArray())),
            DictLit d => Node("dict", args: d.Entries.SelectMany(entry => new[]
            {
                new HirArgument("key", LowerExpression(entry.Key, analysis)),
                new HirArgument("value", LowerExpression(entry.Value, analysis)),
            }).ToList()),
            StructLitExpr s => Node("struct", s.TypeName,
                s.Fields.Select(field => new HirArgument(field.Field,
                    LowerExpression(field.Value, analysis))).ToList()),
            IndexExpr i => Node("index", args: Args(i.Target, i.Index)),
            FieldExpr f => Node("field", f.Field, Args(f.Target)),
            BinaryExpr b => Node("binary", b.Op, Args(b.Left, b.Right)),
            LogicalExpr l => Node("logical", l.Op, Args(l.Left, l.Right)),
            UnaryExpr u => Node("unary", u.Op, Args(u.Operand)),
            InputExpr i => Node("input", args: Args(i.Prompt)),
            CallExpr c => Node("call", c.Name, Args(c.Args.ToArray())),
            NamespacedCallExpr c => Node("call", $"{c.Ns}.{c.Func}", Args(c.Args.ToArray())),
            MethodCallExpr m => Node("method", m.Method,
                new[] { m.Target }.Concat(m.Args).Select(value =>
                    new HirArgument(null, LowerExpression(value, analysis))).ToList()),
            LambdaExpr l => Node("lambda", body: LowerBlock(l.Body, analysis),
                args: l.Params.Select(param => new HirArgument(param,
                    new HirExpression("parameter", "dynamic", param, [], null, l.Line))).ToList()),
            _ => Node("unknown", expr.GetType().Name),
        };

        HirExpression Node(string op, string? value = null, List<HirArgument>? args = null,
            HirBlock? body = null) => new(op, type, value, args ?? [], body, expr.Line);
    }

    private static string StatementType(Statement statement, TypeAnalysis analysis) =>
        analysis.StatementTypes.TryGetValue(statement, out var type) ? PrintableType(type) : "dynamic";

    private static string ExpressionType(Expr expression, TypeAnalysis analysis)
    {
        if (analysis.ExpressionTypes.TryGetValue(expression, out var type)) return PrintableType(type);
        return expression switch
        {
            StringLit or TemplateStringExpr => "string",
            NumberLit n => Math.Truncate(n.Value) == n.Value ? "int_literal" : "float_literal",
            BoolLit => "bool",
            NullLit => "none",
            ListLit => "list",
            DictLit => "dict",
            LambdaExpr => "fn",
            _ => "dynamic",
        };
    }

    private static string PrintableType(string type) => type switch
    {
        "<integer literal>" => "int_literal",
        "<float literal>" => "float_literal",
        _ => type,
    };
}

static class TypedHirFormatter
{
    public static string Format(HirProgram program)
    {
        var output = new StringBuilder("mako.hir 1\n");
        foreach (var global in program.Globals)
            output.Append("\nglobal %").Append(global.Name).Append(": ").Append(global.Type)
                .Append(" = ").AppendLine(FormatExpression(global.Initializer));
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
                .Append(") -> ").Append(fn.ReturnType).AppendLine(" {");
            WriteBlock(output, fn.Body, 1);
            output.AppendLine("}");
        }
        output.AppendLine("\nmain() -> none {");
        WriteBlock(output, program.Main, 1);
        output.AppendLine("}");
        return output.ToString();
    }

    private static void WriteBlock(StringBuilder output, HirBlock block, int depth)
    {
        foreach (var stmt in block.Statements) WriteStatement(output, stmt, depth);
    }

    private static void WriteStatement(StringBuilder output, HirStatement stmt, int depth)
    {
        string pad = new(' ', depth * 4);
        output.Append(pad).Append(stmt.Op);
        if (stmt.Target != null) output.Append(" %").Append(stmt.Target);
        if (stmt.Type != null) output.Append(": ").Append(stmt.Type);
        if (stmt.Detail != null) output.Append(" [").Append(stmt.Detail).Append(']');
        if (stmt.Operands.Count > 0)
            output.Append(' ').Append(string.Join(", ", stmt.Operands.Select(FormatExpression)));

        if (stmt.Regions.Count == 0)
        {
            output.AppendLine();
            return;
        }

        for (var i = 0; i < stmt.Regions.Count; i++)
        {
            output.AppendLine(i == 0 ? " {" : $"{pad}region {i} {{");
            WriteBlock(output, stmt.Regions[i], depth + 1);
            output.Append(pad).AppendLine("}");
        }
    }

    private static string FormatExpression(HirExpression expr)
    {
        var name = expr.Value == null ? expr.Op : $"{expr.Op}.{expr.Value}";
        var args = expr.Arguments.Count == 0 ? "" :
            $"({string.Join(", ", expr.Arguments.Select(arg =>
                arg.Label == null ? FormatExpression(arg.Value) : $"{arg.Label}: {FormatExpression(arg.Value)}"))})";
        var body = expr.Body == null ? "" : " { ... }";
        return $"{name}{args}{body}: {expr.Type}";
    }
}
