using Raylib_cs;

namespace Mako;

/// Inputs — unified input package, works alongside any MakoRay/Mako2D/Mako3D window.
///
///   using Mako3D;
///   using Inputs;
///
///   main() {
///       Mako3D.init(800, 600, "Game");
///       while Mako3D.running() {
///           if Inputs.key_down("W")           { ... }
///           if Inputs.key_pressed("SPACE")    { ... }
///           if Inputs.mouse_down("middle")    { ... }
///           dx = Inputs.mouse_delta_x();
///           scroll = Inputs.scroll();
///           Mako3D.begin(); ... Mako3D.end();
///       }
///   }
///
static class MakoInputs
{
    // ── Keyboard ──────────────────────────────────────────────────────────────

    public static object? KeyDown(List<object?> a)     => (object?)(bool)Raylib.IsKeyDown(ToKey(a[0]));
    public static object? KeyPressed(List<object?> a)  => (object?)(bool)Raylib.IsKeyPressed(ToKey(a[0]));
    public static object? KeyReleased(List<object?> a) => (object?)(bool)Raylib.IsKeyReleased(ToKey(a[0]));
    public static object? KeyUp(List<object?> a)       => (object?)(bool)Raylib.IsKeyUp(ToKey(a[0]));

    /// last_key() — returns the name of the most recently pressed key, or "" if none.
    public static object? LastKey(List<object?> _)
    {
        int k = Raylib.GetKeyPressed();
        if (k == 0) return (object?)"";
        return (object?)KeyName((KeyboardKey)k);
    }

    /// any_key() — true if any key was pressed this frame.
    public static object? AnyKey(List<object?> _) => (object?)(Raylib.GetKeyPressed() != 0);

    // ── Mouse position ────────────────────────────────────────────────────────

    public static object? MouseX(List<object?> _)       => (object?)(double)Raylib.GetMouseX();
    public static object? MouseY(List<object?> _)       => (object?)(double)Raylib.GetMouseY();
    public static object? MouseDeltaX(List<object?> _)  => (object?)(double)Raylib.GetMouseDelta().X;
    public static object? MouseDeltaY(List<object?> _)  => (object?)(double)Raylib.GetMouseDelta().Y;
    public static object? Scroll(List<object?> _)       => (object?)(double)Raylib.GetMouseWheelMove();

    // ── Mouse buttons ─────────────────────────────────────────────────────────

    public static object? MouseDown(List<object?> a)    => (object?)(bool)Raylib.IsMouseButtonDown(ToBtn(a[0]));
    public static object? MousePressed(List<object?> a) => (object?)(bool)Raylib.IsMouseButtonPressed(ToBtn(a[0]));
    public static object? MouseReleased(List<object?> a)=> (object?)(bool)Raylib.IsMouseButtonReleased(ToBtn(a[0]));
    public static object? MouseUp(List<object?> a)      => (object?)(bool)Raylib.IsMouseButtonUp(ToBtn(a[0]));

    // ── Cursor control ────────────────────────────────────────────────────────

    public static object? HideCursor(List<object?> _)   { Raylib.DisableCursor(); return null; }
    public static object? ShowCursor(List<object?> _)   { Raylib.EnableCursor();  return null; }
    public static object? LockCursor(List<object?> _)   { Raylib.DisableCursor(); return null; }
    public static object? UnlockCursor(List<object?> _) { Raylib.EnableCursor();  return null; }

    // ── Gamepad (basic) ───────────────────────────────────────────────────────

    public static object? GamepadReady(List<object?> a)
    {
        int id = a.Count > 0 ? (int)Convert.ToDouble(a[0]) : 0;
        return (object?)(bool)Raylib.IsGamepadAvailable(id);
    }

    public static object? GamepadBtn(List<object?> a)
    {
        int pad = a.Count > 0 ? (int)Convert.ToDouble(a[0]) : 0;
        int btn = a.Count > 1 ? (int)Convert.ToDouble(a[1]) : 0;
        return (object?)(bool)Raylib.IsGamepadButtonDown(pad, (GamepadButton)btn);
    }

    public static object? GamepadAxis(List<object?> a)
    {
        int pad  = a.Count > 0 ? (int)Convert.ToDouble(a[0]) : 0;
        int axis = a.Count > 1 ? (int)Convert.ToDouble(a[1]) : 0;
        return (object?)(double)Raylib.GetGamepadAxisMovement(pad, (GamepadAxis)axis);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// Canonical key-name → KeyboardKey mapping, shared by all rendering packages.
    internal static KeyboardKey ToKey(object? v)
    {
        if (v is string s)
        {
            if (s.Length == 1) return (KeyboardKey)(int)char.ToUpper(s[0]);
            return s.ToUpper() switch
            {
                "SPACE"     => KeyboardKey.Space,
                "ENTER"     => KeyboardKey.Enter,
                "RETURN"    => KeyboardKey.Enter,
                "ESCAPE"    => KeyboardKey.Escape,
                "ESC"       => KeyboardKey.Escape,
                "UP"        => KeyboardKey.Up,
                "DOWN"      => KeyboardKey.Down,
                "LEFT"      => KeyboardKey.Left,
                "RIGHT"     => KeyboardKey.Right,
                "SHIFT"     => KeyboardKey.LeftShift,
                "LSHIFT"    => KeyboardKey.LeftShift,
                "RSHIFT"    => KeyboardKey.RightShift,
                "CTRL"      => KeyboardKey.LeftControl,
                "LCTRL"     => KeyboardKey.LeftControl,
                "RCTRL"     => KeyboardKey.RightControl,
                "ALT"       => KeyboardKey.LeftAlt,
                "LALT"      => KeyboardKey.LeftAlt,
                "RALT"      => KeyboardKey.RightAlt,
                "TAB"       => KeyboardKey.Tab,
                "BACKSPACE" => KeyboardKey.Backspace,
                "DELETE"    => KeyboardKey.Delete,
                "INSERT"    => KeyboardKey.Insert,
                "HOME"      => KeyboardKey.Home,
                "END"       => KeyboardKey.End,
                "PAGEUP"    => KeyboardKey.PageUp,
                "PAGEDOWN"  => KeyboardKey.PageDown,
                "F1"  => KeyboardKey.F1,  "F2"  => KeyboardKey.F2,
                "F3"  => KeyboardKey.F3,  "F4"  => KeyboardKey.F4,
                "F5"  => KeyboardKey.F5,  "F6"  => KeyboardKey.F6,
                "F7"  => KeyboardKey.F7,  "F8"  => KeyboardKey.F8,
                "F9"  => KeyboardKey.F9,  "F10" => KeyboardKey.F10,
                "F11" => KeyboardKey.F11, "F12" => KeyboardKey.F12,
                _     => KeyboardKey.Null,
            };
        }
        return (KeyboardKey)(int)Convert.ToDouble(v);
    }

    private static MouseButton ToBtn(object? v)
    {
        if (v is string s) return s.ToLower() switch
        {
            "left"   => MouseButton.Left,
            "right"  => MouseButton.Right,
            "middle" => MouseButton.Middle,
            "back"   => MouseButton.Back,
            "forward"=> MouseButton.Forward,
            _        => MouseButton.Left,
        };
        return (MouseButton)(int)Convert.ToDouble(v);
    }

    private static string KeyName(KeyboardKey k) => k switch
    {
        KeyboardKey.Space     => "SPACE",
        KeyboardKey.Enter     => "ENTER",
        KeyboardKey.Escape    => "ESCAPE",
        KeyboardKey.Tab       => "TAB",
        KeyboardKey.Backspace => "BACKSPACE",
        KeyboardKey.Up        => "UP",
        KeyboardKey.Down      => "DOWN",
        KeyboardKey.Left      => "LEFT",
        KeyboardKey.Right     => "RIGHT",
        KeyboardKey.LeftShift => "SHIFT",
        _ => ((int)k is >= 65 and <= 90) ? ((char)(int)k).ToString() : ((int)k).ToString(),
    };

    // ── Dispatch table ────────────────────────────────────────────────────────

    public static readonly Dictionary<string, Func<List<object?>, object?>> Funcs = new()
    {
        ["key_down"]      = KeyDown,
        ["key_pressed"]   = KeyPressed,
        ["key_released"]  = KeyReleased,
        ["key_up"]        = KeyUp,
        ["last_key"]      = LastKey,
        ["any_key"]       = AnyKey,
        ["mouse_x"]       = MouseX,
        ["mouse_y"]       = MouseY,
        ["mouse_delta_x"] = MouseDeltaX,
        ["mouse_delta_y"] = MouseDeltaY,
        ["scroll"]        = Scroll,
        ["mouse_down"]    = MouseDown,
        ["mouse_pressed"] = MousePressed,
        ["mouse_released"]= MouseReleased,
        ["mouse_up"]      = MouseUp,
        ["hide_cursor"]   = HideCursor,
        ["show_cursor"]   = ShowCursor,
        ["lock_cursor"]   = LockCursor,
        ["unlock_cursor"] = UnlockCursor,
        ["gamepad_ready"] = GamepadReady,
        ["gamepad_btn"]   = GamepadBtn,
        ["gamepad_axis"]  = GamepadAxis,
    };
}
