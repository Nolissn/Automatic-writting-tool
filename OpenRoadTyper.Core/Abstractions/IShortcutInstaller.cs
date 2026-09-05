#nullable enable

namespace OpenRoadTyper.Core.Abstractions;

/// <summary>
/// Creates (once) a launcher icon for the app, using whatever mechanism is
/// idiomatic on the current OS: a .lnk file on the Windows desktop, or a
/// freedesktop .desktop entry on Linux.
/// </summary>
public interface IShortcutInstaller
{
    /// <summary>
    /// Ensures a launcher shortcut/entry exists, migrating it from
    /// <paramref name="legacyDisplayName"/> if that is the only one present.
    /// Must never throw - shortcut creation should never block app startup.
    /// </summary>
    void EnsureShortcut(string displayName, string legacyDisplayName, string executablePath);
}
