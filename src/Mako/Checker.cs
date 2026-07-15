namespace Mako;

/// One issue found by `mko check`. The command exits non-zero when issues are
/// present, so typed MAKO can use it as a compile-time gate while untyped
/// scripts remain fully backwards compatible.
record CheckIssue(int Line, string Message);

/// `mko check file.mko` combines the opt-in systems type checker with two
/// low-false-positive lint rules: unused locals and unreachable code after
/// return/break/continue. Undefined-variable-use remains a runtime diagnostic
/// until scope and closure analysis can report it without false positives.
static class Checker
{
    public static List<CheckIssue> Check(ProgramNode program)
    {
        var issues = new List<CheckIssue>();
        SystemsTypeChecker.Check(program, issues);
        CheckFunctionBody(program.Body, issues);
        foreach (var fn in program.Functions)
            CheckFunctionBody(fn.Body, issues);
        return issues;
    }

    private static void CheckFunctionBody(List<Statement> body, List<CheckIssue> issues)
    {
        CheckBlock(body, issues);
        CheckUnusedLocals(body, issues);
    }

    // ── Unreachable code ────────────────────────────────────────────────────

    private static void CheckBlock(List<Statement> block, List<CheckIssue> issues)
    {
        bool sawTerminator = false;
        foreach (var stmt in block)
        {
            if (sawTerminator)
            {
                issues.Add(new CheckIssue(stmt.Line,
                    $"unreachable code — this can never run, it follows a return/break/continue in the same block"));
                // Only report the first unreachable statement per block —
                // once one is flagged, everything after it is redundantly
                // unreachable too and would just be noise.
                break;
            }

            switch (stmt)
            {
                case ReturnStmt or BreakStmt or ContinueStmt:
                    sawTerminator = true;
                    break;
                case IfStmt ifs:
                    CheckBlock(ifs.Then, issues);
                    if (ifs.Else.Count > 0) CheckBlock(ifs.Else, issues);
                    break;
                case WhileStmt ws:
                    CheckBlock(ws.Body, issues);
                    break;
                case ForStmt fs:
                    CheckBlock(fs.Body, issues);
                    break;
                case TryStmt ts:
                    CheckBlock(ts.Try, issues);
                    if (ts.HasCatch) CheckBlock(ts.Catch, issues);
                    break;
            }
        }
    }

    // ── Unused local variables ───────────────────────────────────────────────

    private static void CheckUnusedLocals(List<Statement> body, List<CheckIssue> issues)
    {
        var assigned = new Dictionary<string, int>(); // name -> first assignment line
        var used = new HashSet<string>();
        CollectAssignments(body, assigned);
        CollectUses(body, used);
        foreach (var (name, line) in assigned)
            if (!used.Contains(name) && !name.StartsWith('_')) // leading underscore = "intentionally unused"
                issues.Add(new CheckIssue(line, $"'{name}' is assigned but never used"));
    }

    private static void CollectAssignments(List<Statement> block, Dictionary<string, int> assigned)
    {
        foreach (var stmt in block)
        {
            switch (stmt)
            {
                case AssignStmt a when !assigned.ContainsKey(a.Name):
                    assigned[a.Name] = a.Line;
                    break;
                case ForStmt fs when !assigned.ContainsKey(fs.Var):
                    assigned[fs.Var] = fs.Line;
                    CollectAssignments(fs.Body, assigned);
                    break;
                case ForStmt fs:
                    CollectAssignments(fs.Body, assigned);
                    break;
                case IfStmt ifs:
                    CollectAssignments(ifs.Then, assigned);
                    CollectAssignments(ifs.Else, assigned);
                    break;
                case WhileStmt ws:
                    CollectAssignments(ws.Body, assigned);
                    break;
                case TryStmt ts:
                    CollectAssignments(ts.Try, assigned);
                    if (ts.HasCatch) CollectAssignments(ts.Catch, assigned);
                    break;
            }
        }
    }

    // Walks every expression reachable from the block and records every
    // identifier read — deliberately over-approximates (e.g. also counts
    // the left side of "x = x + 1;" as a use, which is correct: reading x
    // to compute its own new value is a real use, not dead assignment).
    private static void CollectUses(List<Statement> block, HashSet<string> used)
    {
        foreach (var stmt in block)
        {
            switch (stmt)
            {
                case PrintStmt p: UseExpr(p.Value, used); break;
                case PrintnlStmt p: UseExpr(p.Value, used); break;
                case AssignStmt a: UseExpr(a.Value, used); break;
                case IndexAssignStmt ia:
                    used.Add(ia.Name);
                    foreach (var idx in ia.Indices) UseExpr(idx, used);
                    UseExpr(ia.Value, used);
                    break;
                case FieldAssignStmt fa: UseExpr(fa.Target, used); UseExpr(fa.Value, used); break;
                case IfStmt ifs:
                    UseExpr(ifs.Condition, used);
                    CollectUses(ifs.Then, used);
                    CollectUses(ifs.Else, used);
                    break;
                case WhileStmt ws: UseExpr(ws.Condition, used); CollectUses(ws.Body, used); break;
                case ForStmt fs: UseExpr(fs.Iterable, used); CollectUses(fs.Body, used); break;
                case ReturnStmt { Value: not null } r: UseExpr(r.Value, used); break;
                case RunStmt rs: UseExpr(rs.Command, used); break;
                case ConstStmt cs: UseExpr(cs.Value, used); break;
                case TryStmt ts:
                    CollectUses(ts.Try, used);
                    if (ts.HasCatch) CollectUses(ts.Catch, used);
                    break;
                case ThrowStmt th: UseExpr(th.Message, used); break;
                case ExprStmt es: UseExpr(es.Value, used); break;
            }
        }
    }

    private static void UseExpr(Expr expr, HashSet<string> used)
    {
        switch (expr)
        {
            case IdentExpr id: used.Add(id.Name); break;
            case TemplateStringExpr t: UseExpr(t.Expanded, used); break;
            case ListLit l: foreach (var item in l.Items) UseExpr(item, used); break;
            case DictLit d: foreach (var (k, v) in d.Entries) { UseExpr(k, used); UseExpr(v, used); } break;
            case IndexExpr ix: UseExpr(ix.Target, used); UseExpr(ix.Index, used); break;
            case FieldExpr f: UseExpr(f.Target, used); break;
            case MethodCallExpr m: UseExpr(m.Target, used); foreach (var a in m.Args) UseExpr(a, used); break;
            case StructLitExpr sl: foreach (var (_, v) in sl.Fields) UseExpr(v, used); break;
            case BinaryExpr b: UseExpr(b.Left, used); UseExpr(b.Right, used); break;
            case LogicalExpr lo: UseExpr(lo.Left, used); UseExpr(lo.Right, used); break;
            case UnaryExpr u: UseExpr(u.Operand, used); break;
            case InputExpr inp: UseExpr(inp.Prompt, used); break;
            // A call may resolve to a variable holding a lambda, so the
            // callee name itself is a read as well as each argument.
            case CallExpr c: used.Add(c.Name); foreach (var a in c.Args) UseExpr(a, used); break;
            case NamespacedCallExpr nc: foreach (var a in nc.Args) UseExpr(a, used); break;
            case LambdaExpr lam: CollectUses(lam.Body, used); break;
        }
    }
}
