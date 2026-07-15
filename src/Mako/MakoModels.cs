namespace Mako;

/// A small named-model facade over Mako3D. Games keep readable names instead
/// of passing numeric GPU handles through every draw call.
static class MakoModels
{
    private static readonly Dictionary<string, double> Models = new(StringComparer.OrdinalIgnoreCase);

    public static void Reset() => Models.Clear();

    public static object? Load(List<object?> a)
    {
        string name = Text(a, 0, "");
        string path = Text(a, 1, "");
        if (name.Length == 0) throw new MakoError("Models.load(): give the model a name");
        if (path.Length == 0) throw new MakoError("Models.load(): give the model file path");
        double handle = Convert.ToDouble(MakoRay3D.LoadModelFile([path]));
        Models[name] = handle;
        return name;
    }

    public static object? Has(List<object?> a) => Models.ContainsKey(Text(a, 0, ""));

    public static object? Draw(List<object?> a)
    {
        string name = Text(a, 0, "");
        if (!Models.TryGetValue(name, out double handle))
            throw new MakoError($"Models.draw(): model '{name}' was not loaded — use Models.load(\"{name}\", \"file.glb\") first");
        var drawArgs = new List<object?> { handle };
        for (int i = 1; i < a.Count; i++) drawArgs.Add(a[i]);
        return MakoRay3D.DrawModelHandle(drawArgs);
    }

    private static string Text(List<object?> a, int index, string fallback) =>
        a.Count > index && a[index] != null ? a[index]!.ToString() ?? fallback : fallback;

    public static readonly Dictionary<string, Func<List<object?>, object?>> Funcs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["load"] = Load,
        ["has"] = Has,
        ["draw"] = Draw,
    };
}
