using System.Collections.Generic;
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
        if (document is null)
            return new DiagramDocument();
        document.Nodes ??= new List<DiagramNode>();
        foreach (var node in document.Nodes)
            node.Members ??= new List<DiagramMember>();
        return document;
    }
}
