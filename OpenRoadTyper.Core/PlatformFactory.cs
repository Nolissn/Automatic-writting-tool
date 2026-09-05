#nullable enable

using System;
using OpenRoadTyper.Core.Abstractions;
using OpenRoadTyper.Core.PlatformLinux;
using OpenRoadTyper.Core.PlatformWindows;

namespace OpenRoadTyper.Core;

/// <summary>
/// Single choke point for OS branching. Every platform-dependent service is
/// selected here and nowhere else, so the rest of the app (and both UI
/// projects) can depend purely on the Abstractions interfaces without
/// scattering `if (OperatingSystem.IsWindows())` checks around.
/// </summary>
public static class PlatformFactory
{
    public static IKeyboardInputService CreateKeyboardInputService()
    {
        return OperatingSystem.IsWindows()
            ? new WindowsKeyboardInputService()
            : new LinuxKeyboardInputService();
    }

    public static IShortcutInstaller CreateShortcutInstaller()
    {
        return OperatingSystem.IsWindows()
            ? new WindowsShortcutInstaller()
            : new LinuxShortcutInstaller();
    }

    /// <summary>
    /// Speech recognition is the one exception to "one implementation per OS
    /// picked here": Windows uses the built-in System.Speech/SAPI engine,
    /// which lives in the WinForms project itself because the System.Speech
    /// package only targets Windows TFMs and cannot be referenced from this
    /// cross-platform library. Non-Windows platforms get Vosk from here.
    /// </summary>
    public static ISpeechRecognitionService CreateNonWindowsSpeechRecognitionService()
    {
        return new VoskSpeechRecognitionService();
    }
}
