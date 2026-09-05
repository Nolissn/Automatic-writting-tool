#nullable enable

using System.Threading;

namespace OpenRoadTyper.Core.Abstractions;

/// <summary>
/// Sends text into whatever window currently has OS input focus, simulating
/// real keystrokes. Implementations are platform-specific (see
/// PlatformWindows/WindowsKeyboardInputService.cs and
/// PlatformLinux/LinuxKeyboardInputService.cs); obtain the right one for the
/// current OS via <see cref="PlatformFactory"/>.
/// </summary>
public interface IKeyboardInputService
{
    /// <summary>
    /// Types <paramref name="text"/> character by character into the focused window.
    /// </summary>
    /// <param name="text">The payload to type.</param>
    /// <param name="keyDelayMs">Delay between keystrokes, in milliseconds. 0 means "as fast as possible".</param>
    /// <param name="useEnterKey">Whether embedded newlines should be sent as an Enter keypress.</param>
    /// <param name="cancellationToken">Allows aborting mid-transmission.</param>
    void SendText(string text, double keyDelayMs, bool useEnterKey, CancellationToken cancellationToken);
}
