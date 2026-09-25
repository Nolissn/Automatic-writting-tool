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

    /// <summary>
    /// Checks whether this service can actually type right now, without
    /// sending anything. Callers should run this before starting a
    /// countdown so a missing dependency (e.g. no keyboard-injection tool
    /// installed on Linux) surfaces immediately instead of after the user
    /// has waited through the whole countdown for nothing.
    /// </summary>
    /// <param name="unavailableReason">
    /// Set to a human-readable explanation when this returns <see
    /// langword="false"/>; <see langword="null"/> otherwise.
    /// </param>
    /// <returns><see langword="true"/> if <see cref="SendText"/> is expected to work.</returns>
    /// <remarks>
    /// Default implementation always reports readiness, which is correct
    /// for backends with no external dependency (Windows' SendInput).
    /// Backends that shell out to an external tool (Linux) override this.
    /// </remarks>
    bool TryPrepare(out string? unavailableReason)
    {
        unavailableReason = null;
        return true;
    }

    /// <summary>
    /// Triggers any OS permission prompt for synthetic input right away, so
    /// the user can answer it at app startup rather than when the first
    /// countdown runs out. May block until the prompt is answered; call it
    /// off the UI thread.
    /// </summary>
    /// <remarks>
    /// Default implementation does nothing, which is correct for backends
    /// that never prompt (Windows' SendInput).
    /// </remarks>
    void RequestPermission()
    {
    }
}
