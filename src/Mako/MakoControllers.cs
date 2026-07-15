using Raylib_cs;
using System.Runtime.InteropServices;

namespace Mako;

/// Named controller input for games that need more than Players' common path.
static class MakoControllers
{
    public static object? Connected(List<object?> a) => Raylib.IsGamepadAvailable(Id(a));
    public static unsafe object? Name(List<object?> a)
    {
        int id = Id(a);
        if (!Raylib.IsGamepadAvailable(id)) return "";
        return Marshal.PtrToStringUTF8((nint)Raylib.GetGamepadName(id)) ?? "Controller";
    }
    public static object? LeftX(List<object?> a) => Axis(a, GamepadAxis.LeftX);
    public static object? LeftY(List<object?> a) => Axis(a, GamepadAxis.LeftY);
    public static object? RightX(List<object?> a) => Axis(a, GamepadAxis.RightX);
    public static object? RightY(List<object?> a) => Axis(a, GamepadAxis.RightY);
    public static object? Down(List<object?> a) => Button(a, false);
    public static object? Pressed(List<object?> a) => Button(a, true);

    private static object Axis(List<object?> a, GamepadAxis axis)
    {
        float value = Raylib.IsGamepadAvailable(Id(a)) ? Raylib.GetGamepadAxisMovement(Id(a), axis) : 0;
        return (double)(Math.Abs(value) < 0.15f ? 0 : value);
    }

    private static bool Button(List<object?> a, bool pressed)
    {
        int id = Id(a);
        if (!Raylib.IsGamepadAvailable(id)) return false;
        string name = a.Count > 1 ? a[1]?.ToString()?.ToLowerInvariant() ?? "action" : "action";
        var button = name switch
        {
            "a" or "jump" => GamepadButton.RightFaceDown,
            "b" or "back" => GamepadButton.RightFaceRight,
            "x" or "action" => GamepadButton.RightFaceLeft,
            "y" => GamepadButton.RightFaceUp,
            "start" or "pause" => GamepadButton.MiddleRight,
            "select" => GamepadButton.MiddleLeft,
            "up" => GamepadButton.LeftFaceUp,
            "down" => GamepadButton.LeftFaceDown,
            "left" => GamepadButton.LeftFaceLeft,
            "right" => GamepadButton.LeftFaceRight,
            "l1" => GamepadButton.LeftTrigger1,
            "r1" => GamepadButton.RightTrigger1,
            _ => throw new MakoError($"Controllers: unknown button '{name}'"),
        };
        return pressed ? Raylib.IsGamepadButtonPressed(id, button) : Raylib.IsGamepadButtonDown(id, button);
    }

    private static int Id(List<object?> a)
    {
        int player = a.Count > 0 ? (int)Convert.ToDouble(a[0]) : 1;
        if (player < 1 || player > 4) throw new MakoError("Controllers: player number must be from 1 to 4");
        return player - 1;
    }

    public static readonly Dictionary<string, Func<List<object?>, object?>> Funcs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["connected"] = Connected, ["name"] = Name,
        ["left_x"] = LeftX, ["left_y"] = LeftY, ["right_x"] = RightX, ["right_y"] = RightY,
        ["down"] = Down, ["pressed"] = Pressed,
    };
}
