using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Vizstrap.Core.Logging;

namespace Vizstrap.Core.Discord;

/// <summary>
/// Sets the Rich Presence of the running Discord app through its local IPC pipe
/// (<c>\\.\pipe\discord-ipc-0</c> … <c>-9</c>). Keeps the latest activity and sends it whenever
/// Discord is (re)connected, retrying while Discord isn't running. The activity belongs to this
/// process: Discord removes it when the process or the connection goes away.
/// </summary>
public sealed class DiscordIpcClient : IAsyncDisposable
{
    /// <summary>
    /// Bloxstrap's Discord application, named "Roblox", with a "roblox" image. Placeholder until
    /// Vizstrap has its own application in the Discord Developer Portal (same name and image key).
    /// </summary>
    public const string ApplicationId = "1005469189907173486";

    private const string LogSource = nameof(DiscordIpcClient);

    private const int OpHandshake = 0;
    private const int OpFrame = 1;
    private const int OpClose = 2;
    private const int OpPing = 3;
    private const int OpPong = 4;

    private const int MaxFrameLength = 64 * 1024;

    private readonly string _clientId;
    private readonly string _pipePrefix;
    private readonly int _processId;
    private readonly TimeSpan _retryInterval;
    private readonly SemaphoreSlim _wake = new(0);
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();

    private DiscordActivity? _activity;
    private int _version;
    private int _sentVersion;
    private Task? _loop;

    /// <param name="pipePrefix">Pipe names are this plus 0–9; tests use their own.</param>
    /// <param name="retryInterval">Wait before looking for Discord again.</param>
    public DiscordIpcClient(string clientId = ApplicationId, string pipePrefix = "discord-ipc-",
        int? processId = null, TimeSpan? retryInterval = null)
    {
        _clientId = clientId;
        _pipePrefix = pipePrefix;
        _processId = processId ?? Environment.ProcessId;
        _retryInterval = retryInterval ?? TimeSpan.FromSeconds(15);
    }

    public bool IsConnected { get; private set; }

    /// <summary>The Discord user who answered the handshake ("name" or "name#0000"), once connected.</summary>
    public string? ConnectedUser { get; private set; }

    public void Start() => _loop ??= Task.Run(() => RunAsync(_stop.Token));

    /// <summary>Shows <paramref name="activity"/>, or clears the presence for null. Only the latest one is sent.</summary>
    public void SetActivity(DiscordActivity? activity)
    {
        lock (_gate)
        {
            _activity = activity;
            _version++;
        }

        _wake.Release();
    }

    /// <summary>Waits until the latest activity reached Discord; false when not connected or timed out.</summary>
    public async Task<bool> WaitUntilSentAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            lock (_gate)
            {
                if (_sentVersion == _version && IsConnected)
                    return true;
            }

            await Task.Delay(20);
        }

        return false;
    }

    /// <summary>Clears the presence (when connected) and disconnects.</summary>
    public async ValueTask DisposeAsync()
    {
        if (IsConnected)
        {
            SetActivity(null);
            await WaitUntilSentAsync(TimeSpan.FromSeconds(1));
        }

        _stop.Cancel();

        if (_loop is not null)
            await _loop;

        _stop.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        bool reportedMissing = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = await ConnectAsync(cancellationToken);

                if (pipe is null)
                {
                    if (!reportedMissing)
                        Log.Info(LogSource, "Discord isn't running; will keep checking");

                    reportedMissing = true;
                }
                else
                {
                    reportedMissing = false;
                    await HandshakeAsync(pipe, cancellationToken);
                    await ServeAsync(pipe, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is IOException or JsonException or DiscordIpcException or ObjectDisposedException)
            {
                Log.Warn(LogSource, $"Discord connection ended: {ex.Message}");
            }
            finally
            {
                IsConnected = false;
            }

            try
            {
                await Task.Delay(_retryInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<NamedPipeClientStream?> ConnectAsync(CancellationToken cancellationToken)
    {
        for (int index = 0; index < 10; index++)
        {
            var pipe = new NamedPipeClientStream(".", _pipePrefix + index, PipeDirection.InOut, PipeOptions.Asynchronous);

            try
            {
                await pipe.ConnectAsync(200, cancellationToken);
                return pipe;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException)
            {
                await pipe.DisposeAsync();
            }
        }

        return null;
    }

    private async Task HandshakeAsync(Stream pipe, CancellationToken cancellationToken)
    {
        await WriteFrameAsync(pipe, OpHandshake, writer =>
        {
            writer.WriteNumber("v", 1);
            writer.WriteString("client_id", _clientId);
        }, cancellationToken);

        var (op, payload) = await ReadFrameAsync(pipe, cancellationToken);

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        if (op != OpFrame || !root.TryGetProperty("evt", out var evt) || evt.GetString() != "READY")
            throw new DiscordIpcException($"Discord refused the connection: {Encoding.UTF8.GetString(payload)}");

        ConnectedUser = root.TryGetProperty("data", out var data) && data.TryGetProperty("user", out var user) &&
            user.TryGetProperty("username", out var username) ? username.GetString() : null;

        IsConnected = true;
        Log.Info(LogSource, $"Connected to Discord{(ConnectedUser is null ? "" : $" as {ConnectedUser}")}");
    }

    private async Task ServeAsync(Stream pipe, CancellationToken cancellationToken)
    {
        // anything set before this connection existed still has to be sent
        int sentOnThisConnection = -1;
        var readTask = ReadFrameAsync(pipe, cancellationToken);
        Task? wakeTask = null;

        try
        {
            while (true)
            {
                DiscordActivity? activity;
                int version;

                lock (_gate)
                {
                    activity = _activity;
                    version = _version;
                }

                if (version != sentOnThisConnection)
                {
                    await SendActivityAsync(pipe, activity, cancellationToken);
                    sentOnThisConnection = version;

                    lock (_gate)
                        _sentVersion = version;
                }

                // one wake wait is kept across iterations, so no signal is swallowed by an abandoned wait
                wakeTask ??= _wake.WaitAsync(cancellationToken);

                if (await Task.WhenAny(readTask, wakeTask) == readTask)
                {
                    var (op, payload) = await readTask;
                    await HandleFrameAsync(pipe, op, payload, cancellationToken);
                    readTask = ReadFrameAsync(pipe, cancellationToken);
                }
                else
                {
                    await wakeTask;
                    wakeTask = null;
                }
            }
        }
        finally
        {
            // a read still pending when the connection breaks fails later; nobody needs that error
            _ = readTask.ContinueWith(task => task.Exception, TaskContinuationOptions.OnlyOnFaulted);
        }
    }

    private async Task HandleFrameAsync(Stream pipe, int op, byte[] payload, CancellationToken cancellationToken)
    {
        switch (op)
        {
            case OpPing:
                await WriteRawFrameAsync(pipe, OpPong, payload, cancellationToken);
                break;

            case OpClose:
                throw new DiscordIpcException($"Discord closed the connection: {Encoding.UTF8.GetString(payload)}");

            case OpFrame:
                using (var document = JsonDocument.Parse(payload))
                {
                    if (document.RootElement.TryGetProperty("evt", out var evt) && evt.GetString() == "ERROR")
                        Log.Warn(LogSource, $"Discord rejected an update: {Encoding.UTF8.GetString(payload)}");
                }

                break;
        }
    }

    private Task SendActivityAsync(Stream pipe, DiscordActivity? activity, CancellationToken cancellationToken) =>
        WriteFrameAsync(pipe, OpFrame, writer =>
        {
            writer.WriteString("cmd", "SET_ACTIVITY");
            writer.WriteStartObject("args");
            writer.WriteNumber("pid", _processId);

            // no activity clears the presence
            if (activity is not null)
            {
                writer.WritePropertyName("activity");
                activity.WriteJson(writer);
            }

            writer.WriteEndObject();
            writer.WriteString("nonce", Guid.NewGuid().ToString());
        }, cancellationToken);

    private static async Task WriteFrameAsync(Stream pipe, int op, Action<Utf8JsonWriter> writeBody, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writeBody(writer);
            writer.WriteEndObject();
        }

        await WriteRawFrameAsync(pipe, op, buffer.ToArray(), cancellationToken);
    }

    /// <summary>Frames are an int32 opcode and int32 length (little-endian), then the JSON.</summary>
    private static async Task WriteRawFrameAsync(Stream pipe, int op, byte[] payload, CancellationToken cancellationToken)
    {
        var frame = new byte[8 + payload.Length];
        BinaryPrimitives.WriteInt32LittleEndian(frame, op);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), payload.Length);
        payload.CopyTo(frame, 8);

        await pipe.WriteAsync(frame, cancellationToken);
        await pipe.FlushAsync(cancellationToken);
    }

    private static async Task<(int Op, byte[] Payload)> ReadFrameAsync(Stream pipe, CancellationToken cancellationToken)
    {
        var header = new byte[8];
        await pipe.ReadExactlyAsync(header, cancellationToken);

        int op = BinaryPrimitives.ReadInt32LittleEndian(header);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));

        if (length is < 0 or > MaxFrameLength)
            throw new DiscordIpcException($"Unexpected frame length {length}");

        var payload = new byte[length];
        await pipe.ReadExactlyAsync(payload, cancellationToken);

        return (op, payload);
    }
}

public sealed class DiscordIpcException(string message) : Exception(message);
