using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Vizstrap.Core.Tests.TestSupport;

/// <summary>
/// Plays the Discord app's side of its IPC pipe: answers the handshake with READY, acknowledges
/// commands and records every frame the client sends.
/// </summary>
internal sealed class FakeDiscord : IAsyncDisposable
{
    private readonly NamedPipeServerStream _server;
    private readonly Channel<(int Op, JsonElement Json)> _frames = Channel.CreateUnbounded<(int, JsonElement)>();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _serve;

    /// <param name="prefix">Pipe name prefix given to the client; the fake listens on "{prefix}{index}".</param>
    public FakeDiscord(string prefix, int index = 0)
    {
        _server = new NamedPipeServerStream(prefix + index, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        _serve = ServeAsync(_stop.Token);
    }

    public static string NewPrefix() => $"vizstrap-test-{Guid.NewGuid():N}-";

    /// <summary>The next frame the client sent (the handshake first).</summary>
    public async Task<(int Op, JsonElement Json)> NextFrameAsync(TimeSpan? timeout = null)
    {
        using var cancellation = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));
        return await _frames.Reader.ReadAsync(cancellation.Token);
    }

    public Task SendAsync(int op, string json) => WriteFrameAsync(op, Encoding.UTF8.GetBytes(json));

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();

        try
        {
            await _serve;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException)
        {
        }

        await _server.DisposeAsync();
    }

    private async Task ServeAsync(CancellationToken cancellationToken)
    {
        await _server.WaitForConnectionAsync(cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            var header = new byte[8];
            await _server.ReadExactlyAsync(header, cancellationToken);

            int op = BinaryPrimitives.ReadInt32LittleEndian(header);
            var payload = new byte[BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4))];
            await _server.ReadExactlyAsync(payload, cancellationToken);

            var json = JsonDocument.Parse(payload).RootElement.Clone();
            await _frames.Writer.WriteAsync((op, json), cancellationToken);

            if (op == 0)
                await SendAsync(1, """{"cmd":"DISPATCH","evt":"READY","data":{"v":1,"user":{"id":"1","username":"tester"}}}""");
            else if (op == 1)
                await SendAsync(1, $$"""{"cmd":"{{json.GetProperty("cmd").GetString()}}","evt":null,"data":{},"nonce":"{{json.GetProperty("nonce").GetString()}}"}""");
        }
    }

    private async Task WriteFrameAsync(int op, byte[] payload)
    {
        var frame = new byte[8 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, op);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), payload.Length);
        payload.CopyTo(frame, 8);

        await _server.WriteAsync(frame);
        await _server.FlushAsync();
    }
}
