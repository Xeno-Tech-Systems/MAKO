namespace Mako;

/// Builds ANIX Plan v1 JSON. This package never reads or changes system state;
/// the ANIX core owns validation, elevation, persistence, and activation.
static class MakoAnix
{
    private static readonly List<object?> Operations = [];

    public static void Reset() => Operations.Clear();

    public static object? Set(List<object?> a)
    {
        Require(a, 2, "ANIX.set");
        Operations.Add(new Dictionary<string, object?> { ["op"] = "set", ["key"] = Text(a[0]), ["value"] = a[1] });
        return null;
    }

    public static object? Enable(List<object?> a) => Feature(a, "enable", "ANIX.enable");
    public static object? Disable(List<object?> a) => Feature(a, "disable", "ANIX.disable");

    private static object? Feature(List<object?> a, string op, string call)
    {
        Require(a, 1, call);
        Operations.Add(new Dictionary<string, object?> { ["op"] = op, ["feature"] = Text(a[0]) });
        return null;
    }

    public static object? Package(List<object?> a)
    {
        Require(a, 1, "ANIX.package");
        Operations.Add(new Dictionary<string, object?> { ["op"] = "package.add", ["name"] = Text(a[0]) });
        return null;
    }

    public static object? RemovePackage(List<object?> a)
    {
        Require(a, 1, "ANIX.remove_package");
        Operations.Add(new Dictionary<string, object?> { ["op"] = "package.remove", ["name"] = Text(a[0]) });
        return null;
    }

    public static object? Plan(List<object?> _)
    {
        return new Dictionary<string, object?> {
            ["planVersion"] = 1d,
            ["language"] = "mako",
            ["operations"] = new List<object?>(Operations),
        };
    }

    public static object? Finish(List<object?> _)
    {
        Console.WriteLine(Json.Encode(Plan([])));
        return null;
    }

    private static void Require(List<object?> a, int count, string call)
    {
        if (a.Count != count) throw new MakoError($"{call}() expects {count} argument{(count == 1 ? "" : "s")}");
    }

    private static string Text(object? value)
    {
        string text = value?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(text)) throw new MakoError("ANIX names must not be empty");
        return text;
    }

    public static readonly Dictionary<string, Func<List<object?>, object?>> Funcs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["set"] = Set, ["enable"] = Enable, ["disable"] = Disable,
        ["package"] = Package, ["remove_package"] = RemovePackage,
        ["plan"] = Plan, ["finish"] = Finish,
    };
}
