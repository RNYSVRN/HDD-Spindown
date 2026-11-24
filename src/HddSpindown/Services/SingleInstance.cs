using System.IO;
using System.IO.Pipes;

namespace HddSpindown.Services;

/// <summary>Ensures a single running instance; further launches pass their arguments on via a named pipe.</summary>
public sealed class SingleInstance : IDisposable
{
    private const string Name = "HddSpindown-7c1e2b";
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _cts = new();

    public bool IsFirst { get; }

    public SingleInstance()
    {
        _mutex = new Mutex(true, @"Local\" + Name, out bool created);
        IsFirst = created;
    }

    public static bool Forward(IReadOnlyList<string> args)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", Name, PipeDirection.Out);
            pipe.Connect(3000);
            using var w = new StreamWriter(pipe);
            w.Write(string.Join('\n', args.Count > 0 ? args : ["--show"]));
            return true;
        }
        catch { return false; }
    }

    public void Listen(Action<string[]> onArgs)
    {
        Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    await using var pipe = new NamedPipeServerStream(Name, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await pipe.WaitForConnectionAsync(_cts.Token);
                    using var r = new StreamReader(pipe);
                    string text = await r.ReadToEndAsync(_cts.Token);
                    onArgs(text.Split('\n', StringSplitOptions.RemoveEmptyEntries));
                }
                catch (OperationCanceledException) { break; }
                catch { await Task.Delay(500); }
            }
        });
    }

    public void Dispose()
    {
        _cts.Cancel();
        if (IsFirst) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
