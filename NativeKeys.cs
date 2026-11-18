using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Input;

namespace NoSvMania;

/// <summary>
/// Reads whether a key is currently held, even when this window is not focused
/// (osu! has focus while you play). It only reads key state; it installs no hooks.
/// MonoGame's Keys enum values match Windows virtual-key codes.
/// </summary>
internal static class NativeKeys
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    public static bool IsDown(Keys key) => (GetAsyncKeyState((int)key) & 0x8000) != 0;
}
