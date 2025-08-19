using System.Collections.ObjectModel;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using PausePrint.Interop;

namespace PausePrint.UI.Ipc;

public sealed class NamedPipeClient : IAsyncDisposable
{
    private readonly ObservableCollection<PrintJobDto> _collection;
    private readonly CancellationToken _token;
    private const string PipeName = "pauseprint_ipc";
    private NamedPipeClientStream? _client;

    public NamedPipeClient(ObservableCollection<PrintJobDto> collection, CancellationToken token)
    {
        _collection = collection;
        _token = token;
        _ = Task.Run(ConnectAndListenAsync, token);
    }

    private async Task ConnectAndListenAsync()
    {
        while (!_token.IsCancellationRequested)
        {
            try
            {
                _client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await _client.ConnectAsync(2000, _token).ConfigureAwait(false);
                using var reader = new StreamReader(_client, Encoding.UTF8, leaveOpen: true);
                while (!_token.IsCancellationRequested && _client.IsConnected)
                {
                    var line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (line == null) break;
                    var job = JsonSerializer.Deserialize<PrintJobDto>(line);
                    if (job != null)
                    {
                        App.Current.Dispatcher.Invoke(() => _collection.Add(job));
                    }
                }
            }
            catch
            {
                await Task.Delay(1000, _token);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        try { _client?.Dispose(); } catch { }
        return ValueTask.CompletedTask;
    }
}


