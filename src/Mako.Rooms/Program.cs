// Mako.Rooms — the relay server half of docs/rooms-design.md. A small,
// separate program (not part of the mko CLI) that MAKO games' new `Room`
// package connects to. Rooms are pure in-memory state (a room exists only
// as long as at least one player is connected — no database, no
// persistence across restarts, per the design doc's own "simplicity over
// durability for v1" call).
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.UseWebSockets();

var rooms = new RoomRegistry();

app.Map("/ws", async (HttpContext context) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    await ConnectionHandler.Run(socket, rooms);
});

app.Run();

/// One connected player: which room, which player_id within that room.
sealed class Connection
{
    public required WebSocket Socket { get; init; }
    public string? RoomCode { get; set; }
    public int PlayerId { get; set; }
}

/// A room's state: the join code, and every currently-connected player in
/// it. Player IDs are assigned in join order — the room creator is always
/// 1, matching docs/players.md's own "player number, not a raw connection
/// id" convention so Room and Players feel like one consistent idea.
sealed class Room
{
    public required string Code { get; init; }
    public List<Connection> Players { get; } = new();
}

sealed class RoomRegistry
{
    private readonly Dictionary<string, Room> _rooms = new();
    private readonly object _lock = new();
    private readonly Random _random = new();

    private const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ"; // no I/O — easy to misread aloud

    public Room CreateRoom(Connection creator)
    {
        lock (_lock)
        {
            string code;
            do
            {
                code = new string(Enumerable.Range(0, 4).Select(_ => CodeChars[_random.Next(CodeChars.Length)]).ToArray());
            } while (_rooms.ContainsKey(code));

            var room = new Room { Code = code };
            room.Players.Add(creator);
            creator.RoomCode = code;
            creator.PlayerId = 1;
            _rooms[code] = room;
            return room;
        }
    }

    public Room? JoinRoom(string code, Connection joiner)
    {
        lock (_lock)
        {
            if (!_rooms.TryGetValue(code, out var room)) return null;
            int nextId = room.Players.Count + 1;
            room.Players.Add(joiner);
            joiner.RoomCode = code;
            joiner.PlayerId = nextId;
            return room;
        }
    }

    public void Leave(Connection conn)
    {
        lock (_lock)
        {
            if (conn.RoomCode == null) return;
            if (_rooms.TryGetValue(conn.RoomCode, out var room))
            {
                room.Players.RemoveAll(p => p == conn);
                if (room.Players.Count == 0)
                    _rooms.Remove(conn.RoomCode); // no persistence past the last player, per the design doc
            }
            conn.RoomCode = null;
        }
    }

    public IReadOnlyList<Connection> OtherPlayers(Connection conn)
    {
        lock (_lock)
        {
            if (conn.RoomCode == null || !_rooms.TryGetValue(conn.RoomCode, out var room))
                return Array.Empty<Connection>();
            return room.Players.Where(p => p != conn).ToList();
        }
    }
}

static class ConnectionHandler
{
    public static async Task Run(WebSocket socket, RoomRegistry rooms)
    {
        var conn = new Connection { Socket = socket };
        var buffer = new byte[8192];

        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close) break;

                string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                await HandleMessage(json, conn, rooms);
            }
        }
        catch (WebSocketException)
        {
            // Client disconnected uncleanly — treat the same as an explicit
            // leave, not an error the server needs to report anywhere.
        }
        finally
        {
            rooms.Leave(conn);
            // A client that just exits (as a short-lived test script does,
            // rather than sending a proper WebSocket close frame first)
            // leaves the socket in Aborted, not Open/CloseReceived/CloseSent
            // — CloseAsync() only accepts those three and throws otherwise.
            // Found by actually running two real MAKO processes against
            // this server rather than only testing with a clean disconnect.
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived or WebSocketState.CloseSent)
            {
                try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None); }
                catch (WebSocketException) { /* client already gone — nothing to close gracefully */ }
            }
        }
    }

    private static async Task HandleMessage(string json, Connection conn, RoomRegistry rooms)
    {
        JsonElement msg;
        try { msg = JsonDocument.Parse(json).RootElement; }
        catch (JsonException) { await Send(conn, new { type = "error", message = "malformed message" }); return; }

        if (!msg.TryGetProperty("type", out var typeProp)) return;
        string type = typeProp.GetString() ?? "";

        switch (type)
        {
            case "create":
            {
                var room = rooms.CreateRoom(conn);
                await Send(conn, new { type = "created", code = room.Code, player_id = conn.PlayerId });
                break;
            }
            case "join":
            {
                string code = msg.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
                var room = rooms.JoinRoom(code, conn);
                if (room == null)
                {
                    await Send(conn, new { type = "error", message = "room not found" });
                    break;
                }
                await Send(conn, new { type = "joined", code = room.Code, player_id = conn.PlayerId });
                break;
            }
            case "state":
            {
                if (!msg.TryGetProperty("data", out var data)) break;
                var others = rooms.OtherPlayers(conn);
                var payload = new { type = "player_state", player_id = conn.PlayerId, data };
                foreach (var other in others)
                    await Send(other, payload);
                break;
            }
            case "leave":
            {
                var others = rooms.OtherPlayers(conn);
                rooms.Leave(conn);
                var payload = new { type = "player_left", player_id = conn.PlayerId };
                foreach (var other in others)
                    await Send(other, payload);
                break;
            }
        }
    }

    private static async Task Send(Connection conn, object payload)
    {
        if (conn.Socket.State != WebSocketState.Open) return;
        string json = JsonSerializer.Serialize(payload);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        await conn.Socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
    }
}
