using ImGuiNET;
using Raylib_cs;
using System.Numerics;

namespace Mako;

/// Renders Dear ImGui directly inside an already-open raylib window, using
/// raylib's own low-level Rlgl immediate-mode calls instead of a separate
/// GL context. This is what lets MakoUI live INSIDE the same window as
/// Mako3D/Mako2D (a toolbar/panel over the 3D scene) instead of opening a
/// second OS window.
unsafe sealed class ImGuiRaylibController : IDisposable
{
    private nint _ctx;
    private uint _fontTexId;
    private bool _disposed;

    public ImGuiRaylibController()
    {
        _ctx = ImGui.CreateContext();
        ImGui.SetCurrentContext(_ctx);

        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        io.DisplaySize = new Vector2(Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
        io.DeltaTime = 1f / 60f;

        UploadFontAtlas();
    }

    private void UploadFontAtlas()
    {
        var io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out byte* pixels, out int width, out int height);
        _fontTexId = Rlgl.LoadTexture(pixels, width, height, PixelFormat.UncompressedR8G8B8A8, 1);
        io.Fonts.SetTexID((nint)_fontTexId);
        io.Fonts.ClearTexData();
    }

    /// Call once per frame, before issuing any ImGui.* widget calls.
    public void NewFrame()
    {
        var io = ImGui.GetIO();
        io.DisplaySize = new Vector2(Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
        io.DeltaTime = Raylib.GetFrameTime() > 0 ? Raylib.GetFrameTime() : 1f / 60f;

        io.MousePos = Raylib.GetMousePosition();
        io.MouseDown[0] = Raylib.IsMouseButtonDown(MouseButton.Left);
        io.MouseDown[1] = Raylib.IsMouseButtonDown(MouseButton.Right);
        io.MouseDown[2] = Raylib.IsMouseButtonDown(MouseButton.Middle);
        io.MouseWheel = Raylib.GetMouseWheelMove();

        FeedKeyboard(io);

        ImGui.NewFrame();
    }

    /// Call once per frame, after all ImGui.* widget calls and ImGui.Render().
    public void RenderDrawData()
    {
        ImGui.Render();
        var data = ImGui.GetDrawData();
        if (data.CmdListsCount == 0) return;

        Rlgl.DrawRenderBatchActive();
        Rlgl.DisableBackfaceCulling();
        Rlgl.DisableDepthTest();
        Rlgl.EnableScissorTest();

        for (int n = 0; n < data.CmdListsCount; n++)
        {
            var cmdList = data.CmdLists[n];
            var vtxBuffer = cmdList.VtxBuffer;
            var idxBuffer = cmdList.IdxBuffer;

            for (int c = 0; c < cmdList.CmdBuffer.Size; c++)
            {
                var cmd = cmdList.CmdBuffer[c];
                if (cmd.ElemCount == 0) continue;

                var clip = cmd.ClipRect;
                int sx = (int)clip.X, sy = (int)clip.Y;
                int sw = (int)(clip.Z - clip.X), sh = (int)(clip.W - clip.Y);
                if (sw <= 0 || sh <= 0) continue;
                // Raylib's scissor origin is top-left, matching ImGui's — but
                // needs flipping against the actual framebuffer height when
                // DPI scaling differs; for a 1:1 pixel window this is direct.
                Raylib.BeginScissorMode(sx, sy, sw, sh);

                Rlgl.SetTexture((uint)cmd.TextureId);
                Rlgl.Begin(4 /* RL_TRIANGLES */);
                for (int i = 0; i < cmd.ElemCount; i += 3)
                {
                    DrawVertex(vtxBuffer, idxBuffer, (int)(cmd.IdxOffset + i + 0));
                    DrawVertex(vtxBuffer, idxBuffer, (int)(cmd.IdxOffset + i + 1));
                    DrawVertex(vtxBuffer, idxBuffer, (int)(cmd.IdxOffset + i + 2));
                }
                Rlgl.End();

                Raylib.EndScissorMode();
            }
        }

        Rlgl.SetTexture(0);
        Rlgl.DisableScissorTest();
    }

    private static void DrawVertex(ImPtrVector<ImDrawVertPtr> vtx, ImVector<ushort> idx, int idxIndex)
    {
        var v = vtx[idx[idxIndex]];
        byte r = (byte)(v.col & 0xFF);
        byte g = (byte)((v.col >> 8) & 0xFF);
        byte b = (byte)((v.col >> 16) & 0xFF);
        byte a = (byte)((v.col >> 24) & 0xFF);
        Rlgl.Color4ub(r, g, b, a);
        Rlgl.TexCoord2f(v.uv.X, v.uv.Y);
        Rlgl.Vertex2f(v.pos.X, v.pos.Y);
    }

    // ── Keyboard bridge (best-effort common keys) ────────────────────────────

    private static readonly (KeyboardKey rl, ImGuiKey im)[] KeyMap =
    [
        (KeyboardKey.Space, ImGuiKey.Space), (KeyboardKey.Enter, ImGuiKey.Enter),
        (KeyboardKey.Escape, ImGuiKey.Escape), (KeyboardKey.Backspace, ImGuiKey.Backspace),
        (KeyboardKey.Tab, ImGuiKey.Tab), (KeyboardKey.Left, ImGuiKey.LeftArrow),
        (KeyboardKey.Right, ImGuiKey.RightArrow), (KeyboardKey.Up, ImGuiKey.UpArrow),
        (KeyboardKey.Down, ImGuiKey.DownArrow), (KeyboardKey.Delete, ImGuiKey.Delete),
        (KeyboardKey.Home, ImGuiKey.Home), (KeyboardKey.End, ImGuiKey.End),
        (KeyboardKey.LeftShift, ImGuiKey.LeftShift), (KeyboardKey.RightShift, ImGuiKey.RightShift),
        (KeyboardKey.LeftControl, ImGuiKey.LeftCtrl), (KeyboardKey.LeftAlt, ImGuiKey.LeftAlt),
    ];

    private static void FeedKeyboard(ImGuiIOPtr io)
    {
        foreach (var (rl, im) in KeyMap)
            io.AddKeyEvent(im, Raylib.IsKeyDown(rl));

        // Letters/digits for text input fields.
        for (int k = (int)KeyboardKey.A; k <= (int)KeyboardKey.Z; k++)
            io.AddKeyEvent(ImGuiKey.A + (k - (int)KeyboardKey.A), Raylib.IsKeyDown((KeyboardKey)k));
        for (int k = (int)KeyboardKey.Zero; k <= (int)KeyboardKey.Nine; k++)
            io.AddKeyEvent(ImGuiKey._0 + (k - (int)KeyboardKey.Zero), Raylib.IsKeyDown((KeyboardKey)k));

        int c;
        while ((c = Raylib.GetCharPressed()) != 0)
            io.AddInputCharacter((uint)c);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ImGui.DestroyContext(_ctx);
    }
}
