using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace Memoit.Services;

public sealed class SingleInstance : IDisposable
{
    private readonly Mutex mutex;
    private readonly string pipeName;
    private readonly CancellationTokenSource stop = new();
    public bool IsFirst { get; }

    public SingleInstance(string dataPath)
    {
        string identity = WindowsIdentity.GetCurrent().User?.Value + "|" + Path.GetFullPath(dataPath).ToUpperInvariant();
        pipeName = "Memoit-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
        mutex = new Mutex(true, @"Local\" + pipeName, out bool created);
        IsFirst = created;
    }

    public async Task NotifyAsync()
    {
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);
        await client.WriteAsync(new byte[] { 1 });
    }

    public async Task ListenAsync(Action activate, Action<Exception> failed)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                using var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(stop.Token);
                if (await server.ReadAsync(new byte[1], stop.Token) > 0) activate();
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception ex) { failed(ex); }
    }

    public void Dispose()
    {
        stop.Cancel();
        if (IsFirst) mutex.ReleaseMutex();
        mutex.Dispose();
    }
}
