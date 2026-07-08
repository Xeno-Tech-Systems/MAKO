namespace Mako;

/// Net — HTTP requests for MAKO scripts.
///
///   using Net;
///
///   main() {
///       res = Net.get("https://api.example.com/data");
///       if Net.ok(res) {
///           data = json_decode(Net.body(res));
///           print data["field"];
///       }
///   }
///
/// A response is a dict: {"status": 200, "ok": true, "body": "...", "error": ""}
static class MakoNet
{
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(20),
    };

    private static Dictionary<string, object?> Wrap(int status, string body, string error = "") =>
        new()
        {
            ["status"] = (double)status,
            ["ok"]     = status is >= 200 and < 300,
            ["body"]   = body,
            ["error"]  = error,
        };

    private static Dictionary<string, object?> RunRequest(Func<Task<HttpResponseMessage>> send)
    {
        try
        {
            var resp = send().GetAwaiter().GetResult();
            var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return Wrap((int)resp.StatusCode, body);
        }
        catch (Exception ex)
        {
            return Wrap(0, "", ex.Message);
        }
    }

    private static void ApplyHeaders(HttpRequestMessage req, List<object?> a, int fromIndex)
    {
        if (a.Count <= fromIndex || a[fromIndex] is not Dictionary<string, object?> headers) return;
        foreach (var (k, v) in headers)
            req.Headers.TryAddWithoutValidation(k, v?.ToString() ?? "");
    }

    public static object? Get(List<object?> a)
    {
        string url = a.Count > 0 ? a[0]?.ToString() ?? "" : "";
        return RunRequest(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyHeaders(req, a, 1);
            return Client.SendAsync(req);
        });
    }

    public static object? Post(List<object?> a)
    {
        string url  = a.Count > 0 ? a[0]?.ToString() ?? "" : "";
        string body = a.Count > 1 ? a[1]?.ToString() ?? "" : "";
        return RunRequest(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            };
            ApplyHeaders(req, a, 2);
            return Client.SendAsync(req);
        });
    }

    public static object? Put(List<object?> a)
    {
        string url  = a.Count > 0 ? a[0]?.ToString() ?? "" : "";
        string body = a.Count > 1 ? a[1]?.ToString() ?? "" : "";
        return RunRequest(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            };
            ApplyHeaders(req, a, 2);
            return Client.SendAsync(req);
        });
    }

    public static object? Delete(List<object?> a)
    {
        string url = a.Count > 0 ? a[0]?.ToString() ?? "" : "";
        return RunRequest(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Delete, url);
            ApplyHeaders(req, a, 1);
            return Client.SendAsync(req);
        });
    }

    // ── Response helpers (so scripts don't need dict[] everywhere) ───────────

    private static Dictionary<string, object?>? AsResponse(List<object?> a) =>
        a.Count > 0 ? a[0] as Dictionary<string, object?> : null;

    public static object? Ok(List<object?> a) =>
        (object?)(AsResponse(a)?.GetValueOrDefault("ok") is true);

    public static object? Status(List<object?> a) =>
        AsResponse(a)?.GetValueOrDefault("status") ?? 0d;

    public static object? Body(List<object?> a) =>
        AsResponse(a)?.GetValueOrDefault("body") ?? "";

    public static object? Error(List<object?> a) =>
        AsResponse(a)?.GetValueOrDefault("error") ?? "";

    /// json(response) — decode the body as JSON directly
    public static object? DecodeJson(List<object?> a)
    {
        var body = Body(a) as string ?? "";
        try { return Json.Decode(body); }
        catch { return null; }
    }

    // ── Utility ───────────────────────────────────────────────────────────────

    /// url_encode(s) — percent-encode a string for use in a URL
    public static object? UrlEncode(List<object?> a) =>
        (object?)Uri.EscapeDataString(a.Count > 0 ? a[0]?.ToString() ?? "" : "");

    public static object? UrlDecode(List<object?> a) =>
        (object?)Uri.UnescapeDataString(a.Count > 0 ? a[0]?.ToString() ?? "" : "");

    // ── Dispatch table ────────────────────────────────────────────────────────

    public static readonly Dictionary<string, Func<List<object?>, object?>> Funcs = new()
    {
        ["get"]         = Get,
        ["post"]        = Post,
        ["put"]         = Put,
        ["delete"]      = Delete,
        ["ok"]          = Ok,
        ["status"]      = Status,
        ["body"]        = Body,
        ["error"]       = Error,
        ["json"]        = DecodeJson,
        ["url_encode"]  = UrlEncode,
        ["url_decode"]  = UrlDecode,
    };
}
