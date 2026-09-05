#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Text;
using OpenRoadTyper.Core.Abstractions;

namespace OpenRoadTyper.Core.PlatformLinux;

/// <summary>
/// Linux equivalent of the Windows .lnk desktop shortcut: a freedesktop.org
/// .desktop entry. It is installed into ~/.local/share/applications so the
/// app shows up in the desktop's application menu/launcher (GNOME Activities,
/// KDE application menu, etc.) with a proper icon, and mirrored onto
/// ~/Desktop when a desktop folder exists, matching the Windows behavior of
/// placing an icon directly on the desktop.
/// </summary>
[UnsupportedOSPlatform("windows")]
public sealed class LinuxShortcutInstaller : IShortcutInstaller
{
    public void EnsureShortcut(string displayName, string legacyDisplayName, string executablePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                return;
            }

            var appsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "applications");
            Directory.CreateDirectory(appsDir);

            var desktopFileName = "openroadtyper.desktop";
            var desktopFilePath = Path.Combine(appsDir, desktopFileName);
            if (File.Exists(desktopFilePath))
            {
                return;
            }

            RemoveLegacyEntry(appsDir, legacyDisplayName);

            var iconPath = InstallIcon(executablePath);
            var contents = BuildDesktopEntry(displayName, executablePath, iconPath);
            File.WriteAllText(desktopFilePath, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            MakeExecutable(desktopFilePath);
            MarkAsTrusted(desktopFilePath);

            CopyToDesktopFolder(desktopFilePath, desktopFileName);
        }
        catch
        {
            // Shortcut creation should never block startup.
        }
    }

    private static void RemoveLegacyEntry(string appsDir, string legacyDisplayName)
    {
        try
        {
            var legacyPath = Path.Combine(appsDir, "openroadtyper-legacy.desktop");
            if (File.Exists(legacyPath))
            {
                File.Delete(legacyPath);
            }
        }
        catch
        {
            // Non-fatal.
        }

        _ = legacyDisplayName;
    }

    private static string BuildDesktopEntry(string displayName, string executablePath, string? iconPath)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[Desktop Entry]");
        builder.AppendLine("Type=Application");
        builder.AppendLine("Version=1.0");
        builder.AppendLine($"Name={displayName}");
        builder.AppendLine("Comment=Sendet Text per simulierter Tastatureingabe in das aktive Fenster");
        builder.AppendLine($"Exec=\"{executablePath}\"");
        if (!string.IsNullOrEmpty(iconPath))
        {
            builder.AppendLine($"Icon={iconPath}");
        }

        builder.AppendLine("Terminal=false");
        builder.AppendLine("Categories=Utility;Office;");
        return builder.ToString();
    }

    private static string? InstallIcon(string executablePath)
    {
        try
        {
            var sourceDir = Path.GetDirectoryName(executablePath);
            if (sourceDir is null)
            {
                return null;
            }

            // The Avalonia app ships its icon under an Assets/ subfolder (it
            // doubles as an embedded avares:// resource for the window icon);
            // the WinForms app, if it ever shipped one the same way, would be
            // found the same way. Also check the output root for robustness
            // across different publish layouts.
            var sourceIcon = Path.Combine(sourceDir, "Assets", "app-icon.png");
            if (!File.Exists(sourceIcon))
            {
                sourceIcon = Path.Combine(sourceDir, "app-icon.png");
            }

            if (!File.Exists(sourceIcon))
            {
                return null;
            }

            var iconDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "icons", "hicolor", "256x256", "apps");
            Directory.CreateDirectory(iconDir);

            var targetIcon = Path.Combine(iconDir, "openroadtyper.png");
            File.Copy(sourceIcon, targetIcon, overwrite: true);
            return targetIcon;
        }
        catch
        {
            return null;
        }
    }

    private static void MakeExecutable(string filePath)
    {
        try
        {
            var mode = File.GetUnixFileMode(filePath);
            File.SetUnixFileMode(
                filePath,
                mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        }
        catch
        {
            // Non-fatal - some filesystems (or older runtimes) may not support this.
        }
    }

    private static void MarkAsTrusted(string filePath)
    {
        // GNOME/Nautilus refuses to run a .desktop file placed on the desktop
        // unless it is marked trusted. gio is part of glib2.0-bin, present on
        // virtually every Ubuntu desktop install; if it's missing we simply
        // skip this and the launcher still works from the app menu.
        try
        {
            using var process = Process.Start(new ProcessStartInfo("gio")
            {
                ArgumentList = { "set", filePath, "metadata::trusted", "true" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            process?.WaitForExit(2000);
        }
        catch
        {
            // gio not installed - not fatal.
        }
    }

    private static void CopyToDesktopFolder(string desktopFilePath, string desktopFileName)
    {
        try
        {
            var desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktopDir) || !Directory.Exists(desktopDir))
            {
                return;
            }

            var target = Path.Combine(desktopDir, desktopFileName);
            if (File.Exists(target))
            {
                return;
            }

            File.Copy(desktopFilePath, target);
            MakeExecutable(target);
            MarkAsTrusted(target);
        }
        catch
        {
            // Non-fatal - not every environment has a Desktop folder.
        }
    }
}
