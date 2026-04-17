#nullable enable

using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OpenRoadTyper;

internal static class Program
{
    private const string ShortcutDisplayName = "Autotype Terminal";
    private const string LegacyShortcutDisplayName = "OpenRoadTyper";

    [STAThread]
    private static void Main()
    {
        EnsureDesktopShortcut();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }

    private static void EnsureDesktopShortcut()
    {
        try
        {
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktopPath) || !Directory.Exists(desktopPath))
            {
                return;
            }

            var shortcutPath = Path.Combine(desktopPath, $"{ShortcutDisplayName}.lnk");
            if (File.Exists(shortcutPath))
            {
                return;
            }

            var executablePath = Application.ExecutablePath;
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                return;
            }

            TryMigrateLegacyShortcut(desktopPath, shortcutPath, executablePath);
            CreateDesktopShortcut(shortcutPath, executablePath);
        }
        catch
        {
            // Shortcut creation should never block startup.
        }
    }

    private static void TryMigrateLegacyShortcut(string desktopPath, string shortcutPath, string executablePath)
    {
        var legacyShortcutPath = Path.Combine(desktopPath, $"{LegacyShortcutDisplayName}.lnk");
        if (!File.Exists(legacyShortcutPath) || File.Exists(shortcutPath))
        {
            return;
        }

        try
        {
            File.Move(legacyShortcutPath, shortcutPath);
            CreateDesktopShortcut(shortcutPath, executablePath);
        }
        catch
        {
            // If migration fails, the new shortcut will be created separately.
        }
    }

    private static void CreateDesktopShortcut(string shortcutPath, string executablePath)
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
            SetShortcutProperty(shortcut, "Description", ShortcutDisplayName);
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
