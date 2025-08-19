using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using PausePrint.Interop;

namespace PausePrint.Service.Ipc;

public sealed class NamedPipeServer : IAsyncDisposable
{
    private readonly ILogger<NamedPipeServer> _logger;
    private readonly CancellationToken _token;
    private readonly Channel<PrintJobDto> _outgoing;
    private const string PipeName = "pauseprint_ipc";

    private volatile NamedPipeServerStream? _client;

    public NamedPipeServer(ILogger<NamedPipeServer> logger, Channel<PrintJobDto> outgoing, CancellationToken token)
    {
        _logger = logger;
        _outgoing = outgoing;
        _token = token;
        _ = Task.Run(AcceptLoopAsync, token);
        _ = Task.Run(ForwardLoopAsync, token);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_token.IsCancellationRequested)
        {
            try
            {
                var server = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(_token).ConfigureAwait(false);
                _client = server;
                _logger.LogInformation("IPC client connected");
                // No read handling for POC; just keep the stream until it disconnects
                _ = Task.Run(async () =>
                {
                    var buffer = new byte[1];
                    try { await server.ReadAsync(buffer, _token).ConfigureAwait(false); }
                    catch { }
                    finally { _client = null; try { server.Dispose(); } catch { } _logger.LogInformation("IPC client disconnected"); }
                }, _token);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "IPC accept loop error");
                await Task.Delay(1000, _token);
            }
        }
    }

    private async Task ForwardLoopAsync()
    {
        await foreach (var job in _outgoing.Reader.ReadAllAsync(_token))
        {
            var client = _client;
            if (client == null || !client.IsConnected) continue;
            try
            {
                var json = JsonSerializer.Serialize(job);
                var bytes = Encoding.UTF8.GetBytes(json + "\n");
                await client.WriteAsync(bytes, 0, bytes.Length, _token).ConfigureAwait(false);
                await client.FlushAsync(_token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write to IPC client");
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        try { _client?.Dispose(); } catch { }
        return ValueTask.CompletedTask;
    }
}


