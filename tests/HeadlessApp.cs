using Avalonia;
using Avalonia.Headless;
using PiousProjectViewer;

namespace PiousProjectViewer.Tests;

public static class HeadlessApp
{
    static readonly object Gate = new();
    static readonly Queue<Action> Jobs = new();
    static Thread? _thread;

    public static void OnUi(Action action)
    {
        Ensure();
        Exception? error = null;
        using var done = new ManualResetEventSlim(false);
        lock (Jobs)
        {
            Jobs.Enqueue(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                done.Set();
            });
            Monitor.Pulse(Jobs);
        }
        done.Wait();
        if (error is not null)
            throw new Exception(error.Message, error);
    }

    static void Ensure()
    {
        if (_thread is not null)
            return;
        lock (Gate)
        {
            if (_thread is not null)
                return;
            using var started = new ManualResetEventSlim(false);
            var thread = new Thread(() =>
            {
                AppBuilder.Configure<App>()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                    .SetupWithoutStarting();
                started.Set();
                while (true)
                {
                    Action job;
                    lock (Jobs)
                    {
                        while (Jobs.Count == 0)
                            Monitor.Wait(Jobs);
                        job = Jobs.Dequeue();
                    }
                    job();
                }
            });
            thread.IsBackground = true;
            if (OperatingSystem.IsWindows())
                thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            started.Wait();
            _thread = thread;
        }
    }
}
