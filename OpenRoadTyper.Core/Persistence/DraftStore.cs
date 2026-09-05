#nullable enable

using System;
using System.IO;

namespace OpenRoadTyper.Core.Persistence;

/// <summary>
/// Persists the in-progress draft text across sessions. Backed by
/// Environment.SpecialFolder.ApplicationData, which .NET already maps
/// correctly per OS (%AppData%\OpenRoadTyper on Windows,
/// ~/.config/OpenRoadTyper on Linux via XDG_CONFIG_HOME) - no platform
/// abstraction needed here beyond that.
/// </summary>
public static class DraftStore
{
    private const string AppFolderName = "OpenRoadTyper";
    private const string FileName = "draft.txt";

    public static string GetPersistedTextPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppFolderName);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, FileName);
    }

    public static string Load()
    {
        try
        {
            var path = GetPersistedTextPath();
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static void Save(string text)
    {
        try
        {
            File.WriteAllText(GetPersistedTextPath(), text ?? string.Empty);
        }
        catch
        {
            // Persistence must never crash the app.
        }
    }
}
