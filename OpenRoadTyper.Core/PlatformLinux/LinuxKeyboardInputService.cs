#nullable enable

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using OpenRoadTyper.Core.Abstractions;
using OpenRoadTyper.Core.Typing;

namespace OpenRoadTyper.Core.PlatformLinux;

/// <summary>
/// Linux has no equivalent of Windows' SendInput: injecting synthetic
/// keystrokes into whatever window currently has focus is deliberately
/// restricted by the display server, and there is no single blessed API
/// that works the same way across X11 and Wayland. The standard,
/// distro-packaged tools for this job are `xdotool` (X11, and X11 apps
/// running under XWayland) and `ydotool` (talks to the kernel's uinput
/// device directly via a small daemon, so it also works on pure Wayland
/// with no XWayland at all). This service auto-detects which of the two is
/// actually usable in the current session and shells out to it, exactly
/// the way desktop automation tools on Linux normally do this - the user
/// never has to pick a backend by hand.
///
/// When xdotool's own library (libxdo, installed alongside the xdotool
/// package) is present, it is called in-process instead of launching the
/// xdotool executable. That keeps one X connection open for the whole app
/// lifetime, which matters on Wayland desktops (KDE Plasma 6, GNOME):
/// XWayland asks the user for input-emulation permission once per X
/// client, so a fresh xdotool process per run meant a fresh prompt per run.
/// </summary>
public sealed class LinuxKeyboardInputService : IKeyboardInputService
{
    private enum SpecialKey
    {
        Enter,
        Tab,
        Backspace,
    }

    private readonly object _backendLock = new();
    private IBackend? _backend;

    public bool TryPrepare(out string? unavailableReason)
    {
        try
        {
            GetBackend();
            unavailableReason = null;
            return true;
        }
        catch (InvalidOperationException ex)
        {
            unavailableReason = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Sends a lone Shift tap through the persistent libxdo connection so
    /// that XWayland's permission prompt appears right at startup, instead
    /// of in the middle of the first countdown. The grant then stays valid
    /// for as long as the app (and so the connection) is running. The other
    /// backends spawn a new process per run, so there is no lasting grant
    /// to obtain for them up front.
    /// </summary>
    public void RequestPermission()
    {
        if (TryPrepare(out _) && GetBackend() is LibxdoBackend libxdo)
        {
            libxdo.SendNeutralKeystroke();
        }
    }

    private IBackend GetBackend()
    {
        lock (_backendLock)
        {
            return _backend ??= ResolveBackend();
        }
    }

    public void SendText(string text, double keyDelayMs, bool useEnterKey, CancellationToken cancellationToken)
    {
        var backend = GetBackend();
        var delayMs = Math.Max(0, (int)Math.Round(keyDelayMs));
        var buffer = new StringBuilder();

        void FlushBuffer()
        {
            if (buffer.Length == 0)
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            backend.TypeText(buffer.ToString(), delayMs, cancellationToken);
            buffer.Clear();
        }

        foreach (var character in text)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (character)
            {
                case '\r':
                    continue;
                case '\n':
                    FlushBuffer();
                    if (useEnterKey)
                    {
                        backend.SendSpecialKey(SpecialKey.Enter, cancellationToken);
                    }
                    break;
                case '\t':
                    FlushBuffer();
                    backend.SendSpecialKey(SpecialKey.Tab, cancellationToken);
                    break;
                case '\b':
                    FlushBuffer();
                    backend.SendSpecialKey(SpecialKey.Backspace, cancellationToken);
                    break;
                default:
                    buffer.Append(character);
                    continue;
            }

            KeyDelay.Wait(delayMs, cancellationToken);
        }

        FlushBuffer();
    }

    /// <summary>
    /// Picks whichever backend actually matches this session, rather than
    /// always trying the tools in a fixed order:
    /// - `xdotool` needs an X11 connection. A `$DISPLAY` means one is
    ///   available, either because this is a plain X11 session or because
    ///   XWayland is running underneath a Wayland compositor (the common
    ///   case on GNOME/Mutter, KDE/Plasma, etc.) - `xdotool` is the more
    ///   mature, dependency-free choice whenever it can work at all.
    /// - `ydotool` works everywhere (X11 or Wayland) because it injects
    ///   events at the kernel level via uinput, but needs its `ydotoold`
    ///   daemon reachable - so it's the fallback, and the only option on a
    ///   pure Wayland session with no XWayland (no `$DISPLAY`).
    /// </summary>
    private static IBackend ResolveBackend()
    {
        var hasDisplay = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"));
        var xdotoolAvailable = IsToolAvailable("xdotool");
        var ydotoolAvailable = IsToolAvailable("ydotool");

        if (hasDisplay && LibxdoBackend.TryCreate() is { } libxdo)
        {
            return libxdo;
        }

        if (hasDisplay && xdotoolAvailable)
        {
            return new XdotoolBackend();
        }

        if (ydotoolAvailable)
        {
            return new YdotoolBackend();
        }

        if (xdotoolAvailable)
        {
            // No $DISPLAY, so this looks like pure Wayland - but xdotool is
            // installed anyway, so give it a chance rather than refusing
            // outright. It will fail fast with its own error if there
            // really is no X server (e.g. no XWayland) to talk to.
            return new XdotoolBackend();
        }

        throw new InvalidOperationException(BuildUnavailableMessage(hasDisplay));
    }

    private static string BuildUnavailableMessage(bool hasDisplay)
    {
        var intro = "Für die Tastatursimulation unter Linux wird 'xdotool' oder 'ydotool' benötigt, " +
                    "es wurde aber keines der beiden gefunden.";

        if (hasDisplay)
        {
            // DISPLAY is set - X11 or XWayland is available, so xdotool
            // (no daemon needed) is the simpler, recommended choice.
            return $"{intro} Diese Sitzung stellt X11/XWayland bereit. " +
                   "Empfohlen: 'sudo apt install xdotool'. " +
                   "Alternative für reines Wayland: 'sudo apt install ydotool' " +
                   "+ laufender Dienst ('sudo systemctl enable --now ydotool').";
        }

        // No DISPLAY - this looks like pure Wayland with no XWayland, so
        // xdotool cannot connect to anything; ydotool is the only option.
        return $"{intro} Diese Sitzung ist reines Wayland ohne XWayland (keine X11-Anzeige verfügbar). " +
               "Empfohlen: 'sudo apt install ydotool' und den Dienst starten mit " +
               "'sudo systemctl enable --now ydotool'.";
    }

    private static bool IsToolAvailable(string toolName)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(toolName, "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });

            if (process is null)
            {
                return false;
            }

            process.WaitForExit(2000);
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    private interface IBackend
    {
        void TypeText(string text, int delayMs, CancellationToken cancellationToken);

        void SendSpecialKey(SpecialKey key, CancellationToken cancellationToken);
    }

    private sealed class XdotoolBackend : IBackend
    {
        public void TypeText(string text, int delayMs, CancellationToken cancellationToken)
        {
            Run("xdotool", new[] { "type", "--clearmodifiers", "--delay", delayMs.ToString(), "--", text }, cancellationToken);
        }

        public void SendSpecialKey(SpecialKey key, CancellationToken cancellationToken)
        {
            var keyName = key switch
            {
                SpecialKey.Enter => "Return",
                SpecialKey.Tab => "Tab",
                SpecialKey.Backspace => "BackSpace",
                _ => throw new ArgumentOutOfRangeException(nameof(key)),
            };

            Run("xdotool", new[] { "key", "--clearmodifiers", keyName }, cancellationToken);
        }
    }

    /// <summary>
    /// Same keystrokes as <see cref="XdotoolBackend"/>, but through libxdo
    /// directly, over a single X connection kept open for the lifetime of
    /// the app (see the class summary for why that matters on Wayland).
    /// </summary>
    private sealed class LibxdoBackend : IBackend
    {
        private const string LibraryName = "libxdo.so.3";
        private const nuint CurrentWindow = 0;

        private readonly object _gate = new();
        private readonly IntPtr _xdo;

        private LibxdoBackend(IntPtr xdo) => _xdo = xdo;

        public static LibxdoBackend? TryCreate()
        {
            if (!NativeLibrary.TryLoad(LibraryName, out _))
            {
                return null;
            }

            try
            {
                var xdo = xdo_new(null);
                return xdo == IntPtr.Zero ? null : new LibxdoBackend(xdo);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                return null;
            }
        }

        public void SendNeutralKeystroke()
        {
            lock (_gate)
            {
                xdo_send_keysequence_window(_xdo, CurrentWindow, "Shift_L", 0);
            }
        }

        public void TypeText(string text, int delayMs, CancellationToken cancellationToken)
        {
            var delayMicroseconds = (uint)delayMs * 1000;

            // Typed one character at a time (rather than handing libxdo the
            // whole string) so a cancel takes effect immediately.
            WithModifiersCleared(() =>
            {
                foreach (var rune in text.EnumerateRunes())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Check(xdo_enter_text_window(_xdo, CurrentWindow, rune.ToString(), delayMicroseconds), "type");
                }
            });
        }

        public void SendSpecialKey(SpecialKey key, CancellationToken cancellationToken)
        {
            var keyName = key switch
            {
                SpecialKey.Enter => "Return",
                SpecialKey.Tab => "Tab",
                SpecialKey.Backspace => "BackSpace",
                _ => throw new ArgumentOutOfRangeException(nameof(key)),
            };

            cancellationToken.ThrowIfCancellationRequested();
            WithModifiersCleared(() => Check(xdo_send_keysequence_window(_xdo, CurrentWindow, keyName, 0), "key"));
        }

        /// <summary>
        /// Equivalent of xdotool's --clearmodifiers: release any modifier the
        /// user is still physically holding, type, then restore it.
        /// </summary>
        private void WithModifiersCleared(Action action)
        {
            lock (_gate)
            {
                xdo_get_active_modifiers(_xdo, out var activeMods, out var activeModsCount);
                try
                {
                    xdo_clear_active_modifiers(_xdo, CurrentWindow, activeMods, activeModsCount);
                    action();
                }
                finally
                {
                    xdo_set_active_modifiers(_xdo, CurrentWindow, activeMods, activeModsCount);
                    Marshal.FreeHGlobal(activeMods); // malloc()ed by libxdo; FreeHGlobal is free() on Unix
                }
            }
        }

        private static void Check(int result, string operation)
        {
            if (result != 0)
            {
                throw new InvalidOperationException($"libxdo '{operation}' ist mit Code {result} fehlgeschlagen.");
            }
        }

        [DllImport(LibraryName)]
        private static extern IntPtr xdo_new([MarshalAs(UnmanagedType.LPUTF8Str)] string? display);

        [DllImport(LibraryName)]
        private static extern int xdo_enter_text_window(IntPtr xdo, nuint window, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, uint delay);

        [DllImport(LibraryName)]
        private static extern int xdo_send_keysequence_window(IntPtr xdo, nuint window, [MarshalAs(UnmanagedType.LPUTF8Str)] string keysequence, uint delay);

        [DllImport(LibraryName)]
        private static extern int xdo_get_active_modifiers(IntPtr xdo, out IntPtr keys, out int nkeys);

        [DllImport(LibraryName)]
        private static extern int xdo_clear_active_modifiers(IntPtr xdo, nuint window, IntPtr activeMods, int activeModsCount);

        [DllImport(LibraryName)]
        private static extern int xdo_set_active_modifiers(IntPtr xdo, nuint window, IntPtr activeMods, int activeModsCount);
    }

    private sealed class YdotoolBackend : IBackend
    {
        public void TypeText(string text, int delayMs, CancellationToken cancellationToken)
        {
            Run("ydotool", new[] { "type", "--key-delay", delayMs.ToString(), "--", text }, cancellationToken);
        }

        public void SendSpecialKey(SpecialKey key, CancellationToken cancellationToken)
        {
            // ydotool addresses keys by raw Linux input-event codes.
            var code = key switch
            {
                SpecialKey.Enter => 28,     // KEY_ENTER
                SpecialKey.Tab => 15,       // KEY_TAB
                SpecialKey.Backspace => 14, // KEY_BACKSPACE
                _ => throw new ArgumentOutOfRangeException(nameof(key)),
            };

            Run("ydotool", new[] { "key", $"{code}:1", $"{code}:0" }, cancellationToken);
        }
    }

    private static void Run(string fileName, string[] arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException($"'{fileName}' konnte nicht gestartet werden: {ex.Message}", ex);
        }

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Best effort - the process may already have exited.
            }
        });

        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        cancellationToken.ThrowIfCancellationRequested();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"'{fileName}' ist mit Code {process.ExitCode} fehlgeschlagen. {DescribeFailure(fileName, stderr)}".TrimEnd());
        }
    }

    /// <summary>
    /// ydotool's most common failure - its daemon (`ydotoold`) not running,
    /// or running under a different user/socket path than the current
    /// session expects - produces a raw "failed to connect socket" error
    /// that doesn't tell the user what to actually do. Recognize it and
    /// append the fix instead of just surfacing the raw stderr.
    /// </summary>
    private static string DescribeFailure(string fileName, string stderr)
    {
        if (fileName == "ydotool" && stderr.Contains("connect", StringComparison.OrdinalIgnoreCase) &&
            stderr.Contains("socket", StringComparison.OrdinalIgnoreCase))
        {
            return stderr.TrimEnd() + " " +
                   "(Der ydotoold-Dienst läuft vermutlich nicht oder verwendet einen anderen Socket-Pfad. " +
                   "Prüfen/starten mit 'sudo systemctl enable --now ydotool' bzw. " +
                   "die Umgebungsvariable YDOTOOL_SOCKET setzen.)";
        }

        return stderr;
    }
}
