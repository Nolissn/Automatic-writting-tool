#nullable enable

using System;
using Avalonia;
using OpenRoadTyper.Core;

namespace OpenRoadTyper.Avalonia;

internal static class Program
{
    // Avalonia entry point. Kept separate from App startup code so the
    // AppBuilder can be reused by design-time tooling/tests if ever needed.
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            var executablePath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executablePath))
            {
                PlatformFactory.CreateShortcutInstaller()
                    .EnsureShortcut("Autotype Terminal", "OpenRoadTyper", executablePath);
            }
        }
        catch
        {
            // Shortcut creation should never block startup.
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
