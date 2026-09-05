#nullable enable

using System;
using System.Diagnostics;
using System.Threading;

namespace OpenRoadTyper.Core.Typing;

/// <summary>
/// Waits out the configured inter-key delay with sub-millisecond accuracy
/// (a plain Task/Thread sleep is only accurate to ~15ms on most schedulers,
/// which is noticeable for a typing-speed setting). Shared by every
/// <see cref="IKeyboardInputService"/> implementation so millisecond-level
/// pacing behaves identically on Windows and Linux.
/// </summary>
public static class KeyDelay
{
    public static void Wait(double delayMs, CancellationToken cancellationToken)
    {
        if (delayMs <= 0)
        {
            return;
        }

        var wholeMilliseconds = (int)Math.Floor(delayMs);
        if (wholeMilliseconds > 0)
        {
            cancellationToken.WaitHandle.WaitOne(wholeMilliseconds);
            cancellationToken.ThrowIfCancellationRequested();
        }

        var fractionalMilliseconds = delayMs - wholeMilliseconds;
        if (fractionalMilliseconds <= 0)
        {
            return;
        }

        var targetTimestamp = Stopwatch.GetTimestamp() +
            (long)Math.Round(fractionalMilliseconds / 1000d * Stopwatch.Frequency);

        while (Stopwatch.GetTimestamp() < targetTimestamp)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Thread.SpinWait(32);
        }
    }
}
