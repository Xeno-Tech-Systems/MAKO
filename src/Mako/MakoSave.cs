namespace Mako;

/// Per-user local save data. Values are JSON-safe MAKO values and every edit
/// is written atomically, so ordinary games do not need a manual save step.
static class MakoSave
{
    private static string Game = "mako-game";
    private static string FilePath = "";
    private static Dictionary<string, object?> Data = new(StringComparer.OrdinalIgnoreCase);

    public static void Reset()
    {
        Game = "mako-game";
        FilePath = "";
        Data = new(StringComparer.OrdinalIgnoreCase);
    }

    public static object? Open(List<object?> a)
    {
        Game = Slug(Text(a, 0, "mako-game"));
        string root = Environment.GetEnvironmentVariable("MAKO_SAVE_PATH")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mko", "saves");
        Directory.CreateDirectory(root);
        FilePath = Path.Combine(root, Game + ".json");
        Data = new(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(FilePath))
        {
            try
            {
                if (Json.Decode(File.ReadAllText(FilePath)) is Dictionary<string, object?> loaded)
                    Data = new(loaded, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is IOException or FormatException)
            {
                throw new MakoError($"Save.open(): could not read '{FilePath}': {ex.Message}");
            }
        }
        return FilePath;
    }

    public static object? Set(List<object?> a)
    {
        EnsureOpen();
        string key = Text(a, 0, "");
        if (key.Length == 0) throw new MakoError("Save.set(): key must not be empty");
        Data[key] = a.Count > 1 ? a[1] : null;
        Write();
        return Data[key];
    }

    public static object? Get(List<object?> a)
    {
        EnsureOpen();
        string key = Text(a, 0, "");
        return Data.TryGetValue(key, out var value) ? value : a.Count > 1 ? a[1] : null;
    }

    public static object? Has(List<object?> a) { EnsureOpen(); return Data.ContainsKey(Text(a, 0, "")); }
    public static object? All(List<object?> _) { EnsureOpen(); return new Dictionary<string, object?>(Data); }

    public static object? Remove(List<object?> a)
    {
        EnsureOpen();
        bool removed = Data.Remove(Text(a, 0, ""));
        if (removed) Write();
        return removed;
    }

    public static object? Unlock(List<object?> a)
    {
        EnsureOpen();
        string name = Text(a, 0, "");
        if (name.Length == 0) throw new MakoError("Save.unlock(): unlock name must not be empty");
        var unlocks = UnlockList();
        if (!unlocks.Any(x => string.Equals(x?.ToString(), name, StringComparison.OrdinalIgnoreCase)))
        {
            unlocks.Add(name);
            Write();
        }
        return true;
    }

    public static object? Unlocked(List<object?> a)
    {
        EnsureOpen();
        string name = Text(a, 0, "");
        return UnlockList().Any(x => string.Equals(x?.ToString(), name, StringComparison.OrdinalIgnoreCase));
    }

    public static object? Unlocks(List<object?> _) { EnsureOpen(); return new List<object?>(UnlockList()); }
    public static object? PathOf(List<object?> _) { EnsureOpen(); return FilePath; }

    public static object? Clear(List<object?> _)
    {
        EnsureOpen();
        Data.Clear();
        if (File.Exists(FilePath)) File.Delete(FilePath);
        return null;
    }

    private static List<object?> UnlockList()
    {
        if (Data.TryGetValue("unlocks", out var value) && value is List<object?> list) return list;
        var created = new List<object?>();
        Data["unlocks"] = created;
        return created;
    }

    private static void Write()
    {
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, Json.Encode(Data) + "\n");
        File.Move(temp, FilePath, true);
    }

    private static void EnsureOpen()
    {
        if (FilePath.Length == 0) throw new MakoError("Save: call Save.open(\"your-game\") first");
    }

    private static string Text(List<object?> a, int i, string fallback) => a.Count > i && a[i] != null ? a[i]!.ToString() ?? fallback : fallback;
    private static string Slug(string value)
    {
        string slug = new(value.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        slug = slug.Trim('-');
        return slug.Length == 0 ? "mako-game" : slug;
    }

    public static readonly Dictionary<string, Func<List<object?>, object?>> Funcs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["open"] = Open, ["set"] = Set, ["get"] = Get, ["has"] = Has, ["all"] = All,
        ["remove"] = Remove, ["unlock"] = Unlock, ["unlocked"] = Unlocked,
        ["unlocks"] = Unlocks, ["path"] = PathOf, ["clear"] = Clear,
    };
}
