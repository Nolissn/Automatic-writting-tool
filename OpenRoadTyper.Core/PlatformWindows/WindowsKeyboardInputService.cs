#nullable enable

using System;
using System.Runtime.InteropServices;
using OpenRoadTyper.Core.Typing;

namespace OpenRoadTyper.Core.PlatformWindows;

/// <summary>
/// Windows implementation of key transmission via the low-level user32
/// SendInput API (originally the standalone KeyboardTransmitter class).
/// Unicode characters are sent as KEYEVENTF_UNICODE "packets" so the exact
/// glyph is injected regardless of the active keyboard layout; Enter/Tab/
/// Backspace are sent as their real virtual keys so target apps treat them
/// as actual key presses.
/// </summary>
public sealed class WindowsKeyboardInputService : KeyboardTransmitterBase
{
    private const uint InputKeyboard = 1u;
    private const uint KeyEventKeyUp = 0x0002u;
    private const uint KeyEventUnicode = 0x0004u;
    private const ushort VirtualKeyReturn = 0x0D;
    private const ushort VirtualKeyTab = 0x09;
    private const ushort VirtualKeyBack = 0x08;
    private static readonly int InputSize = Marshal.SizeOf(typeof(INPUT));

    protected override void SendUnicodeCharacter(char character)
    {
        SubmitInputs(new[]
        {
            CreateUnicodeInput(character, keyUp: false),
            CreateUnicodeInput(character, keyUp: true),
        });
    }

    protected override void SendEnter() => SendVirtualKey(VirtualKeyReturn);

    protected override void SendTab() => SendVirtualKey(VirtualKeyTab);

    protected override void SendBackspace() => SendVirtualKey(VirtualKeyBack);

    private static void SendVirtualKey(ushort keyCode)
    {
        SubmitInputs(new[]
        {
            CreateVirtualKeyInput(keyCode, keyUp: false),
            CreateVirtualKeyInput(keyCode, keyUp: true),
        });
    }

    private static INPUT CreateUnicodeInput(char character, bool keyUp)
    {
        return new INPUT
        {
            type = InputKeyboard,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = (ushort)character,
                    dwFlags = KeyEventUnicode | (keyUp ? KeyEventKeyUp : 0u),
                    dwExtraInfo = IntPtr.Zero,
                    time = 0,
                },
            },
        };
    }

    private static INPUT CreateVirtualKeyInput(ushort keyCode, bool keyUp)
    {
        return new INPUT
        {
            type = InputKeyboard,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = keyCode,
                    wScan = 0,
                    dwFlags = keyUp ? KeyEventKeyUp : 0u,
                    dwExtraInfo = IntPtr.Zero,
                    time = 0,
                },
            },
        };
    }

    private static void SubmitInputs(INPUT[] inputs)
    {
        var sent = SendInput((uint)inputs.Length, inputs, InputSize);
        if (sent != (uint)inputs.Length)
        {
            var errorCode = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"SendInput fehlgeschlagen. Gesendet: {sent}/{inputs.Length}. Win32-Fehlercode: {errorCode}.");
        }
    }

    [DllImport("user32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    // The union must match the native Win32 INPUT union so sizeof(INPUT) is
    // correct on both x86 and x64. KEYBDINPUT alone is too small on x64.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;

        [FieldOffset(0)]
        public KEYBDINPUT ki;

        [FieldOffset(0)]
        public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }
}
