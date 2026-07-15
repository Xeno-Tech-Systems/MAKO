using System.Globalization;

namespace Mako;

record MirValidationIssue(string Function, string? Block, string Message)
{
    public override string ToString() => Block == null
        ? $"{Function}: {Message}"
        : $"{Function}/{Block}: {Message}";
}

static class MirValidator
{
    public static List<MirValidationIssue> Validate(MirProgram program)
    {
        var issues = new List<MirValidationIssue>();
        var functionNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var function in program.Functions)
        {
            if (!functionNames.Add(function.Name))
                issues.Add(new MirValidationIssue(function.Name, null, "duplicate function"));
            ValidateFunction(function, issues);
        }
        return issues;
    }

    private static void ValidateFunction(MirFunction function, List<MirValidationIssue> issues)
    {
        if (function.Blocks.Count == 0)
        {
            issues.Add(new MirValidationIssue(function.Name, null, "function has no basic blocks"));
            return;
        }

        var labels = new HashSet<string>(StringComparer.Ordinal);
        var definitions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var block in function.Blocks)
        {
            if (!labels.Add(block.Label))
                issues.Add(new MirValidationIssue(function.Name, block.Label, "duplicate block label"));
            foreach (var instruction in block.Instructions)
            {
                if (instruction.Result != null && !definitions.Add(instruction.Result))
                    issues.Add(new MirValidationIssue(function.Name, block.Label,
                        $"temporary '{instruction.Result}' is defined more than once"));
                if (string.IsNullOrWhiteSpace(instruction.Op))
                    issues.Add(new MirValidationIssue(function.Name, block.Label, "instruction has no opcode"));
                if (instruction.Result != null && string.IsNullOrWhiteSpace(instruction.Type))
                    issues.Add(new MirValidationIssue(function.Name, block.Label,
                        $"result '{instruction.Result}' has no type"));
            }
        }

        foreach (var block in function.Blocks)
        {
            if (block.Terminator == null)
            {
                issues.Add(new MirValidationIssue(function.Name, block.Label, "block has no terminator"));
                continue;
            }

            var terminator = block.Terminator;
            foreach (var target in terminator.Targets)
                if (!labels.Contains(target))
                    issues.Add(new MirValidationIssue(function.Name, block.Label,
                        $"terminator targets unknown block '{target}'"));

            var expectedTargets = terminator.Op switch
            {
                "branch" => 2,
                "jump" => 1,
                "return" or "throw" or "trap" => 0,
                _ => -1,
            };
            if (expectedTargets >= 0 && terminator.Targets.Count != expectedTargets)
                issues.Add(new MirValidationIssue(function.Name, block.Label,
                    $"'{terminator.Op}' expects {expectedTargets} target(s), got {terminator.Targets.Count}"));
            if (terminator.Op == "branch" && terminator.Arguments.Count != 1)
                issues.Add(new MirValidationIssue(function.Name, block.Label,
                    $"branch expects one condition, got {terminator.Arguments.Count}"));

            foreach (var argument in block.Instructions.SelectMany(i => i.Arguments)
                         .Concat(terminator.Arguments))
                foreach (var temporary in MirPassUtilities.Temporaries(argument))
                    if (!definitions.Contains(temporary))
                        issues.Add(new MirValidationIssue(function.Name, block.Label,
                            $"uses undefined temporary '{temporary}'"));
        }
    }
}

record MirOptimizationResult(
    MirProgram Program,
    int FoldedInstructions,
    int RemovedInstructions,
    int RemovedBlocks);

static class MirOptimizer
{
    private static readonly HashSet<string> PureOps = new(StringComparer.Ordinal)
    {
        "const", "load", "global.load", "load.field", "load.index",
        "binary.+", "binary.-", "binary.*", "binary./", "binary.%",
        "binary.==", "binary.!=" , "binary.<", "binary.>", "binary.<=", "binary.>=",
        "logical.and", "logical.or", "unary.-", "unary.!",
        "convert.literal", "convert.numeric", "convert.collection", "convert", "cast.checked",
        "list", "dict", "parameter",
    };

    private readonly record struct Constant(string Text, string Type);

    public static MirOptimizationResult Optimize(MirProgram input)
    {
        var program = Clone(input);
        var folded = 0;
        var removedInstructions = 0;
        var removedBlocks = 0;

        foreach (var function in program.Functions)
        {
            folded += FoldConstants(function);
            removedInstructions += RemoveDeadTemporaries(function);
            removedBlocks += RemoveUnreachableBlocks(function);
        }
        return new MirOptimizationResult(program, folded, removedInstructions, removedBlocks);
    }

    private static int FoldConstants(MirFunction function)
    {
        var constants = new Dictionary<string, Constant>(StringComparer.Ordinal);
        var folded = 0;
        foreach (var block in function.Blocks)
        {
            for (var i = 0; i < block.Instructions.Count; i++)
            {
                var instruction = block.Instructions[i];
                if (instruction.Result == null) continue;

                if (instruction.Op == "const" && instruction.Arguments.Count == 1)
                {
                    constants[instruction.Result] = new Constant(instruction.Arguments[0], instruction.Type);
                    continue;
                }

                if (instruction.Op is "convert.literal" or "convert.numeric" &&
                    instruction.Arguments.Count == 1 &&
                    TryConstant(instruction.Arguments[0], constants, out var converted))
                {
                    var text = ConvertConstant(converted.Text, instruction.Type);
                    var replacement = new MirInstruction(instruction.Result, instruction.Type,
                        "const", [text]);
                    block.Instructions[i] = replacement;
                    constants[instruction.Result] = new Constant(text, instruction.Type);
                    folded++;
                    continue;
                }

                if (TryFold(instruction, constants, out var value))
                {
                    var replacement = new MirInstruction(instruction.Result, instruction.Type,
                        "const", [value]);
                    block.Instructions[i] = replacement;
                    constants[instruction.Result] = new Constant(value, instruction.Type);
                    folded++;
                }
            }
            if (block.Terminator is { Op: "branch", Arguments.Count: 1, Targets.Count: 2 } branch &&
                TryConstant(branch.Arguments[0], constants, out var condition) &&
                bool.TryParse(condition.Text, out var selected))
            {
                block.Terminator = new MirTerminator("jump", [],
                    [branch.Targets[selected ? 0 : 1]]);
                folded++;
            }
        }
        return folded;
    }

    private static bool TryFold(MirInstruction instruction,
        Dictionary<string, Constant> constants, out string value)
    {
        value = "";
        if (instruction.Arguments.Count == 1 &&
            instruction.Op is "unary.-" or "unary.!" &&
            TryConstant(instruction.Arguments[0], constants, out var unary))
        {
            if (instruction.Op == "unary.-" && TryNumber(unary.Text, out var number))
            {
                value = (-number).ToString("R", CultureInfo.InvariantCulture);
                return true;
            }
            if (instruction.Op == "unary.!" && bool.TryParse(unary.Text, out var boolean))
            {
                value = (!boolean).ToString().ToLowerInvariant();
                return true;
            }
        }

        if (instruction.Arguments.Count != 2 ||
            !TryConstant(instruction.Arguments[0], constants, out var left) ||
            !TryConstant(instruction.Arguments[1], constants, out var right)) return false;

        if (instruction.Op is "logical.and" or "logical.or" &&
            bool.TryParse(left.Text, out var lb) && bool.TryParse(right.Text, out var rb))
        {
            value = (instruction.Op == "logical.and" ? lb && rb : lb || rb)
                .ToString().ToLowerInvariant();
            return true;
        }

        if (!TryNumber(left.Text, out var l) || !TryNumber(right.Text, out var r)) return false;
        double numeric;
        switch (instruction.Op)
        {
            case "binary.+": numeric = l + r; break;
            case "binary.-": numeric = l - r; break;
            case "binary.*": numeric = l * r; break;
            case "binary./" when r != 0: numeric = l / r; break;
            case "binary.%" when r != 0: numeric = l % r; break;
            case "binary.==": value = (l == r).ToString().ToLowerInvariant(); return true;
            case "binary.!=" : value = (l != r).ToString().ToLowerInvariant(); return true;
            case "binary.<": value = (l < r).ToString().ToLowerInvariant(); return true;
            case "binary.>": value = (l > r).ToString().ToLowerInvariant(); return true;
            case "binary.<=": value = (l <= r).ToString().ToLowerInvariant(); return true;
            case "binary.>=": value = (l >= r).ToString().ToLowerInvariant(); return true;
            default: return false;
        }
        value = numeric.ToString("R", CultureInfo.InvariantCulture);
        return true;
    }

    private static string ConvertConstant(string text, string targetType)
    {
        if (!TryNumber(text, out var number)) return text;
        if (targetType is "i8" or "i16" or "i32" or "i64" or "isize" or
            "u8" or "u16" or "u32" or "u64" or "usize")
            return Math.Truncate(number).ToString("0", CultureInfo.InvariantCulture);
        return number.ToString("R", CultureInfo.InvariantCulture);
    }

    private static bool TryNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static bool TryConstant(string argument, Dictionary<string, Constant> constants,
        out Constant constant)
    {
        var temporary = MirPassUtilities.Temporaries(argument).FirstOrDefault();
        if (temporary != null && constants.TryGetValue(temporary, out constant)) return true;
        constant = default;
        return false;
    }

    private static int RemoveDeadTemporaries(MirFunction function)
    {
        var removed = 0;
        while (true)
        {
            var used = function.Blocks
                .SelectMany(block => block.Instructions.SelectMany(i => i.Arguments)
                    .Concat(block.Terminator?.Arguments ?? []))
                .SelectMany(MirPassUtilities.Temporaries)
                .ToHashSet(StringComparer.Ordinal);
            var passRemoved = 0;
            foreach (var block in function.Blocks)
                for (var i = block.Instructions.Count - 1; i >= 0; i--)
                {
                    var instruction = block.Instructions[i];
                    if (instruction.Result == null || used.Contains(instruction.Result) ||
                        !PureOps.Contains(instruction.Op)) continue;
                    block.Instructions.RemoveAt(i);
                    passRemoved++;
                }
            removed += passRemoved;
            if (passRemoved == 0) return removed;
        }
    }

    private static int RemoveUnreachableBlocks(MirFunction function)
    {
        if (function.Blocks.Count == 0) return 0;
        var byLabel = function.Blocks.ToDictionary(block => block.Label, StringComparer.Ordinal);
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(function.Blocks[0].Label);
        while (pending.Count > 0)
        {
            var label = pending.Pop();
            if (!reachable.Add(label) || !byLabel.TryGetValue(label, out var block)) continue;
            foreach (var target in block.Terminator?.Targets ?? []) pending.Push(target);
            foreach (var handler in block.Instructions.Where(i => i.Op == "try.push")
                         .SelectMany(i => i.Arguments))
                if (byLabel.ContainsKey(handler)) pending.Push(handler);
        }
        var before = function.Blocks.Count;
        function.Blocks.RemoveAll(block => !reachable.Contains(block.Label));
        return before - function.Blocks.Count;
    }

    private static MirProgram Clone(MirProgram input)
    {
        var functions = input.Functions.Select(function =>
        {
            var blocks = function.Blocks.Select(block =>
            {
                var clone = new MirBlock(block.Label);
                clone.Instructions.AddRange(block.Instructions.Select(instruction =>
                    new MirInstruction(instruction.Result, instruction.Type, instruction.Op,
                        [.. instruction.Arguments])));
                if (block.Terminator != null)
                    clone.Terminator = new MirTerminator(block.Terminator.Op,
                        [.. block.Terminator.Arguments], [.. block.Terminator.Targets]);
                return clone;
            }).ToList();
            return new MirFunction(function.Name, [.. function.Parameters], function.ReturnType, blocks);
        }).ToList();
        return new MirProgram([.. input.Structs], functions);
    }
}

static class MirPassUtilities
{
    public static IEnumerable<string> Temporaries(string text)
    {
        var offset = 0;
        while (offset < text.Length)
        {
            var start = text.IndexOf("%t", offset, StringComparison.Ordinal);
            if (start < 0) yield break;
            var end = start + 2;
            while (end < text.Length && char.IsDigit(text[end])) end++;
            if (end > start + 2) yield return text[start..end];
            offset = Math.Max(end, start + 2);
        }
    }
}
