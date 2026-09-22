using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace pious_project_viewer.Diagram;

public sealed class ProjectPulse : IDisposable
{
    FileSystemWatcher? _watcher;
    CancellationTokenSource? _pending;

    public event EventHandler? Due;

    public void WatchFile(string path)
    {
        Stop();
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
            return;
        Directory.CreateDirectory(directory);
        _watcher = new FileSystemWatcher(directory, Path.GetFileName(path))
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnFile;
        _watcher.Created += OnFile;
        _watcher.Renamed += OnFile;
    }

    public void Stop()
    {
        _pending?.Cancel();
        if (_watcher is null)
            return;
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _watcher = null;
    }

    public void Dispose() => Stop();

    void OnFile(object? sender, FileSystemEventArgs args)
    {
        _pending?.Cancel();
        var pending = new CancellationTokenSource();
        _pending = pending;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), pending.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (!pending.IsCancellationRequested)
                Due?.Invoke(this, EventArgs.Empty);
        });
    }
}
