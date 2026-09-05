#nullable enable

using System.Threading;
using OpenRoadTyper.Core.Abstractions;

namespace OpenRoadTyper.Core.Typing;

/// <summary>
/// Shared character-by-character typing loop and inter-key delay timing
/// (originally the Windows-only KeyboardTransmitter). A platform only needs
/// to supply the four low-level key primitives; the iteration, control
/// character handling (\r, \n, \t, \b), cancellation, and sub-millisecond
/// delay accuracy (via a Stopwatch spin-wait for the fractional remainder)
/// are identical everywhere.
/// </summary>
public abstract class KeyboardTransmitterBase : IKeyboardInputService
{
    public void SendText(string text, double keyDelayMs, bool useEnterKey, CancellationToken cancellationToken)
    {
        foreach (var character in text)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (character)
            {
                case '\r':
                    continue;
                case '\n':
                    if (useEnterKey)
                    {
                        SendEnter();
                    }
                    break;
                case '\t':
                    SendTab();
                    break;
                case '\b':
                    SendBackspace();
                    break;
                default:
                    SendUnicodeCharacter(character);
                    break;
            }

            KeyDelay.Wait(keyDelayMs, cancellationToken);
        }
    }

    protected abstract void SendUnicodeCharacter(char character);

    protected abstract void SendEnter();

    protected abstract void SendTab();

    protected abstract void SendBackspace();
}
