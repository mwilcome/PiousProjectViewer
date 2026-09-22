using System.IO;
using System.Text.Json;

namespace pious_project_viewer.Diagram;

public static class DiagramLoader
{
    public static DiagramDocument Load(string path)
    {
        var json = File.ReadAllText(path);
        var document = JsonSerializer.Deserialize<DiagramDocument>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        return document ?? new DiagramDocument();
    }
}
