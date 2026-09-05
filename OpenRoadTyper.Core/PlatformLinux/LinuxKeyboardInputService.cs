#nullable enable

using System;
using System.ComponentModel;
using System.Diagnostics;
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
/// distro-provided tools for this job are `xdotool` (X11, and X11 apps
/// running under XWayland) and `ydotool` (works on pure Wayland too, via a
/// small uinput daemon). This service shells out to whichever is available,
/// exactly the way desktop automation tools on Linux normally do this.
/// </summary>
public sealed class LinuxKeyboardInputService : IKeyboardInputService
{
    private enum SpecialKey
    {
        Enter,
        Tab,
        Backspace,
    }

    private IBackend? _backend;

    public void SendText(string text, double keyDelayMs, bool useEnterKey, CancellationToken cancellationToken)
    {
        var backend = _backend ??= ResolveBackend();
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

    private static IBackend ResolveBackend()
    {
        if (IsToolAvailable("xdotool"))
        {
            return new XdotoolBackend();
        }

        if (IsToolAvailable("ydotool"))
        {
            return new YdotoolBackend();
        }

        throw new InvalidOperationException(
            "Für die Tastatursimulation unter Linux wird 'xdotool' oder 'ydotool' benötigt. " +
            "Installation: 'sudo apt install xdotool' (X11/XWayland, empfohlen) " +
            "oder 'sudo apt install ydotool' + laufender 'ydotoold'-Dienst (reines Wayland).");
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
                $"'{fileName}' ist mit Code {process.ExitCode} fehlgeschlagen. {stderr}".TrimEnd());
        }
    }
}
