using System.Net.WebSockets;
using System.Text;

namespace Mako;

/// Room — networked party play (docs/rooms-design.md). Connects to a
/// small relay server (src/Mako.Rooms) over a WebSocket; a room is a
/// short join code shared between players, and any state a script sends
/// via Room.send() gets relayed to everyone else currently in the room.
///
///   using Room;
///
///   main() {
///       Room.connect("ws://localhost:5000/ws");
///       code = Room.create();
///       print "Room code: " + code;
///       while Room.connected() {
///           Room.send({"x": my_x, "y": my_y});
///           for p in Room.players() {
///               other = Room.state(p);
///           }
///       }
///   }
///
/// Deliberately mirrors Players' own naming register (short verbs, no
/// networking jargon like "socket"/"peer"/"session") — see
/// docs/rooms-design.md for the full protocol and design reasoning.
static class MakoRoom
{
    private static ClientWebSocket? _socket;
    private static CancellationTokenSource? _cts;
    private static Task? _receiveLoop;

    private static int _playerId;
    private static string _roomCode = "";
    private static readonly object _stateLock = new();
    private static readonly HashSet<int> _otherPlayers = new();
    private static readonly Dictionary<int, Dictionary<string, object?>> _playerStates = new();
    private static string _lastError = "";

    public static object? Connect(List<object?> a)
    {
        string url = a.Count > 0 ? a[0]?.ToString() ?? "" : "";
        Disconnect(); // a stale connection from an earlier Room.connect() call must not leak

        _socket = new ClientWebSocket();
        _cts = new CancellationTokenSource();
        try
        {
            _socket.ConnectAsync(new Uri(url), _cts.Token).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            return false;
        }

        _receiveLoop = Task.Run(() => ReceiveLoop(_socket, _cts.Token));
        return true;
    }

    public static object? Create(List<object?> a)
    {
        SendJson(new Dictionary<string, object?> { ["type"] = "create" });
        return WaitFor("created", "code");
    }

    public static object? Join(List<object?> a)
    {
        string code = a.Count > 0 ? a[0]?.ToString() ?? "" : "";
        SendJson(new Dictionary<string, object?> { ["type"] = "join", ["code"] = code });
        return WaitFor("joined", "code");
    }

    public static object? Connected(List<object?> _) =>
        _socket is { State: WebSocketState.Open };

    public static object? Send(List<object?> a)
    {
        var data = a.Count > 0 ? a[0] as Dictionary<string, object?> : null;
        if (data == null) throw new MakoError("Room.send() expects a dict of player state");
        SendJson(new Dictionary<string, object?> { ["type"] = "state", ["data"] = data });
        return null;
    }

    public static object? Players(List<object?> _)
    {
        lock (_stateLock)
            return _otherPlayers.Select(id => (object?)(double)id).ToList();
    }

    public static object? State(List<object?> a)
    {
        int id = a.Count > 0 ? (int)Convert.ToDouble(a[0]) : 0;
        lock (_stateLock)
            return _playerStates.TryGetValue(id, out var state) ? state : null;
    }

    public static object? Leave(List<object?> a)
    {
        SendJson(new Dictionary<string, object?> { ["type"] = "leave" });
        lock (_stateLock) { _otherPlayers.Clear(); _playerStates.Clear(); }
        return null;
    }

    public static object? LastError(List<object?> _) => _lastError;

    // ── Internals ────────────────────────────────────────────────────────

    private static void SendJson(Dictionary<string, object?> message)
    {
        if (_socket is not { State: WebSocketState.Open }) return;
        string json = Json.Encode(message);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None)
            .GetAwaiter().GetResult();
    }

    /// Blocks briefly waiting for a specific server response type after
    /// create()/join() — these are request/response by nature even though
    /// the connection itself is a long-lived socket, so a script can write
    /// `code = Room.create();` and get the code back directly rather than
    /// polling separately for it.
    private static object? WaitFor(string expectedType, string field)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            lock (_stateLock)
            {
                if (_pendingResponse != null && _pendingResponse.Value.Type == expectedType)
                {
                    var value = _pendingResponse.Value.Field == field ? _pendingResponse.Value.Value : "";
                    _pendingResponse = null;
                    return value;
                }
                if (_pendingResponse is { Type: "error" })
                {
                    _lastError = _pendingResponse.Value.Value;
                    _pendingResponse = null;
                    return null;
                }
            }
            Thread.Sleep(10);
        }
        _lastError = "timed out waiting for server response";
        return null;
    }

    private static (string Type, string Field, string Value)? _pendingResponse;

    private static async Task ReceiveLoop(ClientWebSocket socket, CancellationToken token)
    {
        var buffer = new byte[8192];
        try
        {
            while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                if (result.MessageType == WebSocketMessageType.Close) break;

                string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                HandleMessage(json);
            }
        }
        catch (OperationCanceledException) { /* Disconnect() was called — expected */ }
        catch (WebSocketException) { /* connection dropped — nothing more to relay */ }
    }

    private static void HandleMessage(string json)
    {
        object? decoded;
        try { decoded = Json.Decode(json); }
        catch { return; }
        if (decoded is not Dictionary<string, object?> msg) return;

        string type = msg.GetValueOrDefault("type")?.ToString() ?? "";
        switch (type)
        {
            case "created":
            case "joined":
                _playerId = (int)Convert.ToDouble(msg.GetValueOrDefault("player_id") ?? 0d);
                _roomCode = msg.GetValueOrDefault("code")?.ToString() ?? "";
                lock (_stateLock) _pendingResponse = (type, "code", _roomCode);
                break;
            case "error":
                lock (_stateLock) _pendingResponse = ("error", "message", msg.GetValueOrDefault("message")?.ToString() ?? "");
                break;
            case "player_state":
                int fromId = (int)Convert.ToDouble(msg.GetValueOrDefault("player_id") ?? 0d);
                if (msg.GetValueOrDefault("data") is Dictionary<string, object?> data)
                    lock (_stateLock) { _otherPlayers.Add(fromId); _playerStates[fromId] = data; }
                break;
            case "player_left":
                int leftId = (int)Convert.ToDouble(msg.GetValueOrDefault("player_id") ?? 0d);
                lock (_stateLock) { _otherPlayers.Remove(leftId); _playerStates.Remove(leftId); }
                break;
        }
    }

    private static void Disconnect()
    {
        _cts?.Cancel();
        try { _socket?.Dispose(); } catch { /* already closed/disposed */ }
        _socket = null;
        _cts = null;
        lock (_stateLock) { _otherPlayers.Clear(); _playerStates.Clear(); }
    }

    public static void UnloadAll() => Disconnect();

    public static readonly Dictionary<string, Func<List<object?>, object?>> Funcs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["connect"]   = Connect,
        ["create"]    = Create,
        ["join"]      = Join,
        ["connected"] = Connected,
        ["send"]      = Send,
        ["players"]   = Players,
        ["state"]     = State,
        ["leave"]     = Leave,
        ["error"]     = LastError,
    };
}
