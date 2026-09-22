using System;
using System.IO;
using System.Text.Json;

namespace pious_project_viewer.Diagram;

public sealed class Session
{
    public string? Folder { get; set; }
    public string Mode { get; set; } = "complexity";
}

public static class SessionStore
{
    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "pious-project-viewer",
        "session.json");

    public static Session Load()
    {
        if (!File.Exists(FilePath))
            return new Session();
        var session = JsonSerializer.Deserialize<Session>(File.ReadAllText(FilePath));
        return session ?? new Session();
    }

    public static void Save(Session session)
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(session));
    }
}
