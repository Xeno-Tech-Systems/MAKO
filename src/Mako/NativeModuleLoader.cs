namespace Mako;

/// Resolves local `use` modules into one compiler program. Imported functions
/// keep their source namespace in the native symbol (for example
/// `MakoAbi.write`), matching the names already produced for namespaced calls
/// by the HIR lowerer.
static class NativeModuleLoader
{
    public static ProgramNode Load(string entryPath)
    {
        var fullEntry = Path.GetFullPath(entryPath);
        var entry = ParseFile(fullEntry);
        var functions = new List<FnDecl>();
        var symbols = new HashSet<string>(StringComparer.Ordinal);
        var loaded = new HashSet<string>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);

        LoadImports(entry, fullEntry, functions, symbols, loaded, active);
        foreach (var function in entry.Functions)
            AddFunction(function, function.Name, fullEntry, functions, symbols);

        return entry with
        {
            Imports = [],
            Functions = functions,
        };
    }

    private static void LoadImports(ProgramNode owner, string ownerPath,
        List<FnDecl> functions, HashSet<string> symbols, HashSet<string> loaded,
        HashSet<string> active)
    {
        var baseDirectory = Path.GetDirectoryName(ownerPath) ?? ".";
        foreach (var import in owner.Imports)
        {
            var path = Path.GetFullPath(Path.IsPathRooted(import)
                ? import
                : Path.Combine(baseDirectory, import));
            if (loaded.Contains(path)) continue;
            if (!File.Exists(path))
                throw new MakoError($"cannot find native module '{import}'")
                    { SourcePath = ownerPath };
            if (!active.Add(path))
                throw new MakoError($"native module import cycle includes '{path}'")
                    { SourcePath = path };

            var module = ParseFile(path);
            if (module.Namespace is not { Length: > 0 } moduleNamespace)
                throw new MakoError($"native module '{import}' must declare a namespace")
                    { SourcePath = path };
            if (module.Body.Count > 0)
                throw new MakoError($"native module '{import}' cannot contain main or top-level statements")
                    { SourcePath = path };
            if (module.Packages.Count > 0)
                throw new MakoError($"native module '{import}' cannot activate host packages")
                    { SourcePath = path };
            if (module.Constants.Count > 0 || module.Structs.Count > 0)
                throw new MakoError($"native module '{import}' currently supports functions only")
                    { SourcePath = path };

            LoadImports(module, path, functions, symbols, loaded, active);
            foreach (var function in module.Functions)
                AddFunction(function, $"{moduleNamespace}.{function.Name}", path,
                    functions, symbols);
            active.Remove(path);
            loaded.Add(path);
        }
    }

    private static ProgramNode ParseFile(string path)
    {
        string source;
        try { source = File.ReadAllText(path); }
        catch (Exception error)
        {
            throw new MakoError($"cannot read native module '{path}': {error.Message}")
                { SourcePath = path };
        }
        if (source.StartsWith("#!", StringComparison.Ordinal))
        {
            var newline = source.IndexOf('\n');
            source = newline < 0 ? "" : source[(newline + 1)..];
        }
        try { return new Parser(new Lexer(source).Tokenize()).Parse(); }
        catch (MakoError error) when (error.SourcePath is null)
        {
            throw new MakoError(error.RawMessage, error.Line, error.Col, error.Length)
                { SourcePath = path };
        }
    }

    private static void AddFunction(FnDecl function, string symbol, string path,
        List<FnDecl> functions, HashSet<string> symbols)
    {
        if (!symbols.Add(symbol))
            throw new MakoError($"duplicate native symbol '{symbol}'")
                { SourcePath = path };
        var linked = function with { Name = symbol };
        linked.Source = path;
        functions.Add(linked);
    }
}
