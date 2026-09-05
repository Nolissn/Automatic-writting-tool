#nullable enable

using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using OpenRoadTyper.Core.Abstractions;

namespace OpenRoadTyper.Core.PlatformWindows;

/// <summary>
/// Creates a .lnk desktop shortcut via the WScript.Shell COM automation
/// object (originally Program.EnsureDesktopShortcut). This only ever
/// succeeds on Windows, where WScript.Shell is registered.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsShortcutInstaller : IShortcutInstaller
{
    public void EnsureShortcut(string displayName, string legacyDisplayName, string executablePath)
    {
        try
        {
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktopPath) || !Directory.Exists(desktopPath))
            {
                return;
            }

            var shortcutPath = Path.Combine(desktopPath, $"{displayName}.lnk");
            if (File.Exists(shortcutPath))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                return;
            }

            TryMigrateLegacyShortcut(desktopPath, shortcutPath, executablePath, legacyDisplayName, displayName);
            CreateDesktopShortcut(shortcutPath, executablePath, displayName);
        }
        catch
        {
            // Shortcut creation should never block startup.
        }
    }

    private static void TryMigrateLegacyShortcut(
        string desktopPath,
        string shortcutPath,
        string executablePath,
        string legacyDisplayName,
        string displayName)
    {
        var legacyShortcutPath = Path.Combine(desktopPath, $"{legacyDisplayName}.lnk");
        if (!File.Exists(legacyShortcutPath) || File.Exists(shortcutPath))
        {
            return;
        }

        try
        {
            File.Move(legacyShortcutPath, shortcutPath);
            CreateDesktopShortcut(shortcutPath, executablePath, displayName);
        }
        catch
        {
            // If migration fails, the new shortcut will be created separately.
        }
    }

    private static void CreateDesktopShortcut(string shortcutPath, string executablePath, string displayName)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null)
        {
            return;
        }

        object? shell = null;
        object? shortcut = null;

        try
        {
            shell = Activator.CreateInstance(shellType);
            shortcut = shellType.InvokeMember(
                "CreateShortcut",
                BindingFlags.InvokeMethod,
                null,
                shell,
                new object[] { shortcutPath });

            if (shortcut is null)
            {
                return;
            }

            SetShortcutProperty(shortcut, "TargetPath", executablePath);
            SetShortcutProperty(shortcut, "WorkingDirectory", Path.GetDirectoryName(executablePath) ?? string.Empty);
            SetShortcutProperty(shortcut, "Description", displayName);
            SetShortcutProperty(shortcut, "IconLocation", $"{executablePath},0");
            shortcut.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
        }
        finally
        {
            ReleaseComObject(shortcut);
            ReleaseComObject(shell);
        }
    }

    private static void SetShortcutProperty(object shortcut, string propertyName, object value)
    {
        shortcut.GetType().InvokeMember(
            propertyName,
            BindingFlags.SetProperty,
            null,
            shortcut,
            new[] { value });
    }

    private static void ReleaseComObject(object? instance)
    {
        if (instance is not null && Marshal.IsComObject(instance))
        {
            Marshal.FinalReleaseComObject(instance);
        }
    }
}
