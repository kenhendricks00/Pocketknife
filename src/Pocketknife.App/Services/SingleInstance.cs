using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Pocketknife.Core.Settings;

namespace Pocketknife.App.Services;

/// <summary>Keeps one running copy per user and data folder. Later launches hand their command line to it over a named pipe.</summary>
public sealed partial class SingleInstance : IDisposable
{
    // One copy per user and data folder: a copy started with its own POCKETKNIFE_DATA (tests, smoke runs) never
    // hands its command line to the copy someone is using.
    private static readonly string s_name = $"Pocketknife-{Environment.UserName}-{DataFolderId()}";
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _stop = new();

    private SingleInstance(Mutex mutex) => _mutex = mutex;

    /// <summary>Returns null when another copy is already running (after forwarding <paramref name="args"/> to it).</summary>
    public static SingleInstance? TryStart(string[] args)
    {
        var mutex = new Mutex(true, $@"Local\{s_name}", out var first);
        if (first)
        {
            return new SingleInstance(mutex);
        }

        mutex.Dispose();
        try
        {
            AllowSetForegroundWindow(-1);
            using var client = new NamedPipeClientStream(".", s_name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(3000);
            client.Write(Encoding.UTF8.GetBytes(string.Join('\0', args)));
        }
        catch (Exception ex) when (ex is IOException or TimeoutException)
        {
        }

        return null;
    }

    public void Listen(Action<string[]> received) => _ = Task.Run(async () =>
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(s_name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_stop.Token);
                using var reader = new StreamReader(server, Encoding.UTF8);
                var text = await reader.ReadToEndAsync(_stop.Token);
                received(text.Length == 0 ? [] : text.Split('\0'));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
            }
        }
    });

    private static string DataFolderId() =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(AppPaths.DataRoot).ToUpperInvariant())))[..12];

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(int processId);
}
