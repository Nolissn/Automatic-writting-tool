#nullable enable

using System;
using System.IO;
using System.Text.Json;

namespace OpenRoadTyper.Core.Persistence;

/// <summary>
/// The user's default configuration, applied every time the app starts.
/// </summary>
public sealed record AppSettings
{
    public int StartDelaySeconds { get; init; } = 3;
    public decimal TypingDelayMilliseconds { get; init; }
    public bool TypingSpeedUsesSeconds { get; init; } = true;
    public bool MinimizeOnStart { get; init; }
    public bool UseEnterKey { get; init; }
    public string MicLanguage { get; init; } = "de";
}

/// <summary>
/// Persists <see cref="AppSettings"/> as JSON next to the draft text
/// (%AppData%\OpenRoadTyper on Windows, ~/.config/OpenRoadTyper on Linux).
/// </summary>
public static class SettingsStore
{
    private const string AppFolderName = "OpenRoadTyper";
    private const string FileName = "settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string GetSettingsPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppFolderName);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, FileName);
    }

    public static AppSettings Load()
    {
        try
        {
            var path = GetSettingsPath();
            if (!File.Exists(path))
            {
                return new AppSettings();
            }

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            File.WriteAllText(GetSettingsPath(), JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
            // Persistence must never crash the app.
        }
    }
}
