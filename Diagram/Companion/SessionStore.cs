using System;
using System.IO;
using System.Text.Json;

namespace PiousProjectViewer.Diagram;

public sealed class Session
{
    public string? Folder { get; set; }
    public string? RememberedFolder { get; set; }
    public bool RememberProject { get; set; }
    public string Mode { get; set; } = "complexity";
    public string Language { get; set; } = "auto";
    public string Agent { get; set; } = "Grok";
}

public static class SessionStore
{
    internal static string? FilePathOverride { get; set; }

    static string FilePath => FilePathOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "pious-project-viewer",
        "session.json");

    public static Session Load()
    {
        if (!File.Exists(FilePath))
            return new Session();
        try
        {
            var session = JsonSerializer.Deserialize<Session>(File.ReadAllText(FilePath));
            return session ?? new Session();
        }
        catch (JsonException)
        {
            return new Session();
        }
    }

    public static void Save(Session session)
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(session));
    }
}
