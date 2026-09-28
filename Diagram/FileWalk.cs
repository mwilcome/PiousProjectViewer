using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PiousProjectViewer.Diagram;

static class FileWalk
{
    static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "dist", "bin", "obj", "target", "build", ".git", ".angular", ".next", ".svelte-kit"
    };

    public static IEnumerable<string> Ending(string root, string extension) =>
        Collect(root, path => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    public static bool IsDirectory(string path)
    {
        try
        {
            return Directory.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static IEnumerable<string> Named(string root, string fileName) =>
        Collect(root, path => string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase));

    static IEnumerable<string> Collect(string root, Func<string, bool> match)
    {
        if (!Directory.Exists(root))
            yield break;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            List<string> children;
            try
            {
                children = Directory.EnumerateFileSystemEntries(dir).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var child in children)
            {
                bool directory;
                try
                {
                    directory = IsDirectory(child);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
                if (directory)
                {
                    if (!Skip.Contains(Path.GetFileName(child)))
                        pending.Push(child);
                    continue;
                }
                if (match(child))
                    yield return child;
            }
        }
    }
}
