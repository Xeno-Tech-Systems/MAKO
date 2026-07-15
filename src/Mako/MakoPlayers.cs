using Raylib_cs;

namespace Mako;

/// Local multiplayer input with player numbers and words instead of gamepad
/// indices. Player 1 uses WASD, player 2 uses arrows, and connected gamepads
/// work alongside those keys. An idle or virtual controller must never disable
/// the keyboard for that player.
static class MakoPlayers
{
    public static object? Connected(List<object?> a)
    {
        int player = Player(a);
        return player <= 2 || Raylib.IsGamepadAvailable(player - 1);
    }

    public static object? X(List<object?> a)
    {
        int player = Player(a);
        int pad = player - 1;
        bool left = Raylib.IsKeyDown(player == 1 ? KeyboardKey.A : KeyboardKey.Left);
        bool right = Raylib.IsKeyDown(player == 1 ? KeyboardKey.D : KeyboardKey.Right);
        double keyboard = (right ? 1 : 0) - (left ? 1 : 0);
        if (keyboard != 0) return keyboard;
        return Raylib.IsGamepadAvailable(pad)
            ? Deadzone(Raylib.GetGamepadAxisMovement(pad, GamepadAxis.LeftX))
            : 0d;
    }

    public static object? Y(List<object?> a)
    {
        int player = Player(a);
        int pad = player - 1;
        bool up = Raylib.IsKeyDown(player == 1 ? KeyboardKey.W : KeyboardKey.Up);
        bool down = Raylib.IsKeyDown(player == 1 ? KeyboardKey.S : KeyboardKey.Down);
        double keyboard = (down ? 1 : 0) - (up ? 1 : 0);
        if (keyboard != 0) return keyboard;
        return Raylib.IsGamepadAvailable(pad)
            ? Deadzone(Raylib.GetGamepadAxisMovement(pad, GamepadAxis.LeftY))
            : 0d;
    }

    public static object? Down(List<object?> a) => Button(a, false);
    public static object? Pressed(List<object?> a) => Button(a, true);

    private static bool Button(List<object?> a, bool pressed)
    {
        int player = Player(a);
        string action = a.Count > 1 ? a[1]?.ToString()?.ToLowerInvariant() ?? "action" : "action";
        int pad = player - 1;
        KeyboardKey key = (player, action) switch
        {
            (1, "jump") => KeyboardKey.Space,
            (1, "start" or "pause") => KeyboardKey.Escape,
            (1, _) => KeyboardKey.E,
            (2, "jump") => KeyboardKey.Enter,
            (2, "start" or "pause") => KeyboardKey.Backspace,
            (2, _) => KeyboardKey.RightControl,
            _ => KeyboardKey.Null,
        };
        bool keyboard = key != KeyboardKey.Null &&
            (pressed ? Raylib.IsKeyPressed(key) : Raylib.IsKeyDown(key));
        if (keyboard) return true;
        if (!Raylib.IsGamepadAvailable(pad)) return false;

        var button = action switch
        {
            "jump" => GamepadButton.RightFaceDown,
            "back" or "cancel" => GamepadButton.RightFaceRight,
            "start" or "pause" => GamepadButton.MiddleRight,
            _ => GamepadButton.RightFaceLeft,
        };
        return pressed ? Raylib.IsGamepadButtonPressed(pad, button) : Raylib.IsGamepadButtonDown(pad, button);
    }

    private static int Player(List<object?> a)
    {
        int player = a.Count > 0 ? (int)Convert.ToDouble(a[0]) : 1;
        if (player < 1 || player > 4) throw new MakoError("Players: player number must be from 1 to 4");
        return player;
    }

    private static double Deadzone(float value) => Math.Abs(value) < 0.15f ? 0 : value;

    public static readonly Dictionary<string, Func<List<object?>, object?>> Funcs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["connected"] = Connected,
        ["x"] = X,
        ["y"] = Y,
        ["down"] = Down,
        ["pressed"] = Pressed,
    };
}
