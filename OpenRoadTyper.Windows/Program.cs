#nullable enable

using System;
using System.Windows.Forms;
using OpenRoadTyper.Core;

namespace OpenRoadTyper;

internal static class Program
{
    private const string ShortcutDisplayName = "Autotype Terminal";
    private const string LegacyShortcutDisplayName = "OpenRoadTyper";

    [STAThread]
    private static void Main()
    {
        PlatformFactory.CreateShortcutInstaller()
            .EnsureShortcut(ShortcutDisplayName, LegacyShortcutDisplayName, Application.ExecutablePath);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}
