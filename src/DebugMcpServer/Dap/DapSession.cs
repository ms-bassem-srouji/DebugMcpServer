using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.Dap;

internal sealed class DapSession : IDapSession
{
    private readonly Process? _adapterProcess;
    private readonly Stream _inputStream;
    private readonly Stream _outputStream;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _sessionCts = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonNode>> _responseWaiters = new();
    private readonly Channel<DapEvent> _eventChannel;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly TaskCompletionSource _initializedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _seq = 0;
    private Task? _readerTask;
    private bool _disposed;

    /// <summary>Completes when the adapter fires the 'initialized' event.</summary>
    public Task InitializedTask => _initializedTcs.Task;

    private volatile SessionState _state = SessionState.Initializing;
    private volatile int _activeThreadId;
    private volatile bool _hasActiveThread;

    public SessionState State
    {
        get => _state;
        private set => _state = value;
    }

    public int? ActiveThreadId
    {
        get => _hasActiveThread ? _activeThreadId : null;
        set { if (value.HasValue) { _activeThreadId = value.Value; _hasActiveThread = true; } else { _hasActiveThread = false; } }
    }

    public ConcurrentDictionary<string, List<SourceBreakpoint>> Breakpoints { get; } = new(StringComparer.OrdinalIgnoreCase);
    public SemaphoreSlim EventConsumerLock { get; } = new(1, 1);
    public CancellationToken SessionCancellationToken => _sessionCts.Token;
    public ChannelReader<DapEvent> EventChannel => _eventChannel.Reader;

    public DapSession(Process adapterProcess, ILogger logger, int maxPendingEvents = 100)
    {
        _adapterProcess = adapterProcess;
        _inputStream = adapterProcess.StandardInput.BaseStream;
        _outputStream = adapterProcess.StandardOutput.BaseStream;
        _logger = logger;
        _eventChannel = Channel.CreateBounded<DapEvent>(new BoundedChannelOptions(maxPendingEvents)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = true
        });
        _logger.LogInformation("[DapSession] Created. Adapter PID={AdapterPid}, MaxPendingEvents={Max}",
            adapterProcess.Id, maxPendingEvents);
    }

    /// <summary>Test-only constructor that uses raw streams instead of a Process.</summary>
    internal DapSession(Stream inputStream, Stream outputStream, ILogger logger, int maxPendingEvents = 100)
    {
        _inputStream = inputStream;
        _outputStream = outputStream;
        _logger = logger;
        _eventChannel = Channel.CreateBounded<DapEvent>(new BoundedChannelOptions(maxPendingEvents)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = true
        });
    }

    public void StartReaderLoop()
    {
        _logger.LogInformation("[DapSession] Starting reader loop");
        _readerTask = Task.Run(ReaderLoopAsync);
    }

    private async Task ReaderLoopAsync()
    {
        _logger.LogInformation("[DapSession.ReaderLoop] Started on adapter PID={Pid}", _adapterProcess?.Id ?? 0);
        try
        {
            var stream = _outputStream;
            int messageCount = 0;
            while (!_sessionCts.IsCancellationRequested)
            {
                var message = await ReadDapMessageAsync(stream, _sessionCts.Token);
                if (message == null)
                {
                    _logger.LogInformation("[DapSession.ReaderLoop] Adapter stdout closed (EOF) after {Count} messages", messageCount);
                    break;
                }
                messageCount++;
                DispatchMessage(message);
            }
        }
        catch (OperationCanceledException) when (_sessionCts.IsCancellationRequested)
        {
            _logger.LogInformation("[DapSession.ReaderLoop] Cancelled (session disposed)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DapSession.ReaderLoop] Error — adapter may have crashed");
        }
        finally
        {
            var prevState = State;
            State = SessionState.Terminated;
            _logger.LogInformation("[DapSession.ReaderLoop] Cleaning up. Previous state={PrevState}, pending waiters={WaiterCount}",
                prevState, _responseWaiters.Count);
            _eventChannel.Writer.TryComplete();
            _initializedTcs.TrySetException(new DapSessionException("Adapter process terminated before sending 'initialized'"));
            foreach (var (seq, tcs) in _responseWaiters)
            {
                _logger.LogWarning("[DapSession.ReaderLoop] Failing pending waiter seq={Seq}", seq);
                tcs.TrySetException(new DapSessionException("Adapter process terminated unexpectedly"));
            }
            _responseWaiters.Clear();
            _logger.LogInformation("[DapSession.ReaderLoop] Finished");
        }
    }

    private void DispatchMessage(JsonNode message)
    {
        var type = message["type"]?.GetValue<string>();

        if (type == "response")
        {
            var requestSeq = message["request_seq"]?.GetValue<int>() ?? 0;
            var command = message["command"]?.GetValue<string>() ?? "?";
            var success = message["success"]?.GetValue<bool>() ?? false;
            var errMsg = success ? null : message["message"]?.GetValue<string>();

            _logger.LogInformation("[DapSession] <<< RESPONSE: {Command} seq={Seq} success={Success}{Error}",
                command, requestSeq, success,
                errMsg != null ? $" error=\"{errMsg}\"" : "");

            if (_responseWaiters.TryRemove(requestSeq, out var tcs))
            {
                if (success)
                {
                    tcs.TrySetResult(message["body"] ?? new JsonObject());
                }
                else
                {
                    tcs.TrySetException(new DapSessionException(errMsg ?? "DAP request failed"));
                }
            }
            else
            {
                _logger.LogWarning("[DapSession] Response for unknown/expired seq={Seq} command={Command}", requestSeq, command);
            }
        }
        else if (type == "event")
        {
            var eventType = message["event"]?.GetValue<string>() ?? string.Empty;
            var body = message["body"];

            // Log all events, but truncate large bodies (module events are noisy)
            var bodyStr = body?.ToJsonString() ?? "null";
            if (bodyStr.Length > 500) bodyStr = bodyStr[..500] + "...(truncated)";
            _logger.LogInformation("[DapSession] <<< EVENT: {EventType} body={Body}", eventType, bodyStr);

            // Signal initialized TCS
            if (eventType == "initialized")
            {
                _logger.LogInformation("[DapSession] 'initialized' event received — signaling TCS");
                _initializedTcs.TrySetResult();
            }

            // Update session state
            switch (eventType)
            {
                case "stopped":
                    State = SessionState.Paused;
                    var threadId = body?["threadId"]?.GetValue<int>();
                    if (threadId.HasValue) ActiveThreadId = threadId.Value;
                    _logger.LogInformation("[DapSession] State -> Paused, ActiveThreadId={ThreadId}", threadId);
                    break;
                case "continued":
                    State = SessionState.Running;
                    _logger.LogInformation("[DapSession] State -> Running");
                    break;
                case "terminated":
                    State = SessionState.Terminating;
                    _logger.LogInformation("[DapSession] State -> Terminating");
                    break;
            }

            var written = _eventChannel.Writer.TryWrite(new DapEvent(eventType, body));
            if (!written)
                _logger.LogWarning("[DapSession] Failed to write event '{EventType}' to channel (full/closed)", eventType);
        }
        else if (type == "request")
        {
            // Adapter can send reverse requests (e.g., vsdbg handshake).
            var command = message["command"]?.GetValue<string>() ?? string.Empty;
            var seq = message["seq"]?.GetValue<int>() ?? 0;
            _logger.LogInformation("[DapSession] <<< REVERSE REQUEST: {Command} seq={Seq}", command, seq);
            _ = HandleReverseRequestAsync(command, seq, message);
        }
        else
        {
            _logger.LogWarning("[DapSession] <<< UNKNOWN message type={Type}: {Json}",
                type, message.ToJsonString()[..Math.Min(300, message.ToJsonString().Length)]);
        }
    }

    private async Task HandleReverseRequestAsync(string command, int requestSeq, JsonNode message)
    {
        try
        {
            if (command == "handshake")
            {
                var value = message["arguments"]?["value"]?.GetValue<string>() ?? "";
                var response = new JsonObject
                {
                    ["seq"] = Interlocked.Increment(ref _seq),
                    ["type"] = "response",
                    ["request_seq"] = requestSeq,
                    ["command"] = "handshake",
                    ["success"] = true,
                    ["body"] = new JsonObject { ["signature"] = value }
                };
                _logger.LogInformation("[DapSession] Responding to handshake (request_seq={Seq})", requestSeq);
                await WriteDapMessageAsync(response, _sessionCts.Token);
            }
            else if (command == "runInTerminal")
            {
                await HandleRunInTerminalAsync(requestSeq, message);
            }
            else
            {
                _logger.LogWarning("[DapSession] Unhandled reverse request: {Command} — sending generic success", command);
                var response = new JsonObject
                {
                    ["seq"] = Interlocked.Increment(ref _seq),
                    ["type"] = "response",
                    ["request_seq"] = requestSeq,
                    ["command"] = command,
                    ["success"] = true,
                    ["body"] = new JsonObject()
                };
                await WriteDapMessageAsync(response, _sessionCts.Token);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DapSession] Error handling reverse request {Command}", command);
        }
    }

    private async Task HandleRunInTerminalAsync(int requestSeq, JsonNode message)
    {
        try
        {
            var args = message["arguments"];
            var argsArray = args?["args"] as JsonArray;

            if (argsArray == null || argsArray.Count == 0)
            {
                _logger.LogError("[DapSession] runInTerminal: no args provided");
                await SendReverseResponseAsync(requestSeq, "runInTerminal", false, "No args provided");
                return;
            }

            var executable = argsArray[0]?.GetValue<string>() ?? "";
            var cmdArgs = string.Join(" ", argsArray.Skip(1).Select(a =>
            {
                var s = a?.GetValue<string>() ?? "";
                return s.Contains(' ') ? $"\"{s}\"" : s;
            }));

            var cwd = args?["cwd"]?.GetValue<string>();

            _logger.LogInformation("[DapSession] runInTerminal: exe={Exe}, args={Args}, cwd={Cwd}",
                executable, cmdArgs, cwd ?? "(null)");

            var psi = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = cmdArgs,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            if (!string.IsNullOrWhiteSpace(cwd))
                psi.WorkingDirectory = cwd;

            // Copy environment variables from the request
            var env = args?["env"] as JsonObject;
            if (env != null)
            {
                foreach (var (key, value) in env)
                {
                    if (value != null)
                        psi.Environment[key] = value.GetValue<string>();
                }
            }

            var process = Process.Start(psi);
            var pid = process?.Id ?? 0;

            _logger.LogInformation("[DapSession] runInTerminal: launched process PID={Pid}", pid);

            await SendReverseResponseAsync(requestSeq, "runInTerminal", true, null, pid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DapSession] runInTerminal failed");
            await SendReverseResponseAsync(requestSeq, "runInTerminal", false, ex.Message);
        }
    }

    private async Task SendReverseResponseAsync(int requestSeq, string command, bool success, string? errorMessage, int? processId = null)
    {
        var response = new JsonObject
        {
            ["seq"] = Interlocked.Increment(ref _seq),
            ["type"] = "response",
            ["request_seq"] = requestSeq,
            ["command"] = command,
            ["success"] = success
        };
        if (success && processId.HasValue)
            response["body"] = new JsonObject { ["processId"] = processId.Value };
        if (!success && errorMessage != null)
            response["message"] = errorMessage;

        await WriteDapMessageAsync(response, _sessionCts.Token);
    }

    internal static async Task<JsonNode?> ReadDapMessageAsync(Stream stream, CancellationToken ct)
    {
        // Read headers line by line until blank line
        int contentLength = 0;
        var headerBuffer = new List<byte>();

        while (true)
        {
            headerBuffer.Clear();
            while (true)
            {
                int b = await ReadByteAsync(stream, ct);
                if (b == -1) return null; // EOF
                if (b == '\n') break;
                if (b != '\r') headerBuffer.Add((byte)b);
            }

            var line = Encoding.UTF8.GetString(headerBuffer.ToArray());
            if (line.Length == 0) break; // blank line = end of headers

            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(line["Content-Length:".Length..].Trim(), out contentLength))
                    return null; // Malformed Content-Length header
            }
        }

        if (contentLength <= 0) return null;

        // Read body
        var body = new byte[contentLength];
        int totalRead = 0;
        while (totalRead < contentLength)
        {
            int read = await stream.ReadAsync(body.AsMemory(totalRead, contentLength - totalRead), ct);
            if (read == 0) return null; // EOF
            totalRead += read;
        }

        try
        {
            return JsonNode.Parse(Encoding.UTF8.GetString(body));
        }
        catch (JsonException)
        {
            return null; // Malformed JSON body from adapter
        }
    }

    private static async Task<int> ReadByteAsync(Stream stream, CancellationToken ct)
    {
        var buf = new byte[1];
        int read = await stream.ReadAsync(buf.AsMemory(0, 1), ct);
        return read == 0 ? -1 : buf[0];
    }

    public async Task<JsonNode> SendRequestAsync(string command, object? args = null, CancellationToken ct = default)
    {
        var seq = Interlocked.Increment(ref _seq);
        var tcs = new TaskCompletionSource<JsonNode>(TaskCreationOptions.RunContinuationsAsynchronously);
        _responseWaiters[seq] = tcs;

        var message = new JsonObject
        {
            ["seq"] = seq,
            ["type"] = "request",
            ["command"] = command
        };
        if (args != null)
            message["arguments"] = JsonSerializer.SerializeToNode(args);

        var argsJson = args != null ? JsonSerializer.Serialize(args) : "null";
        if (argsJson.Length > 500) argsJson = argsJson[..500] + "...(truncated)";
        _logger.LogInformation("[DapSession] >>> REQUEST: {Command} seq={Seq} args={Args}", command, seq, argsJson);

        await WriteDapMessageAsync(message, ct);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _sessionCts.Token);
        try
        {
            var result = await tcs.Task.WaitAsync(linked.Token);
            _logger.LogInformation("[DapSession] Request {Command} seq={Seq} completed successfully", command, seq);
            return result;
        }
        catch (OperationCanceledException)
        {
            _responseWaiters.TryRemove(seq, out _);
            _logger.LogWarning("[DapSession] Request {Command} seq={Seq} cancelled", command, seq);
            throw;
        }
        catch (Exception ex)
        {
            _responseWaiters.TryRemove(seq, out _);
            _logger.LogWarning("[DapSession] Request {Command} seq={Seq} failed: {Error}", command, seq, ex.Message);
            throw;
        }
    }

    private async Task WriteDapMessageAsync(JsonNode message, CancellationToken ct)
    {
        var json = message.ToJsonString();
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        var header = $"Content-Length: {jsonBytes.Length}\r\n\r\n";
        var headerBytes = Encoding.UTF8.GetBytes(header);

        await _writeLock.WaitAsync(ct);
        try
        {
            var stream = _inputStream;
            await stream.WriteAsync(headerBytes, ct);
            await stream.WriteAsync(jsonBytes, ct);
            await stream.FlushAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void TransitionToRunning() => State = SessionState.Running;
    public void TransitionToTerminating() => State = SessionState.Terminating;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _sessionCts.Cancel();

        if (_adapterProcess != null)
        {
            _logger.LogInformation("[DapSession] Disposing. Adapter PID={Pid}, HasExited={HasExited}",
                _adapterProcess.Id, _adapterProcess.HasExited);

            try
            {
                if (!_adapterProcess.HasExited)
                {
                    _logger.LogInformation("[DapSession] Killing adapter process tree");
                    _adapterProcess.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[DapSession] Error killing adapter process");
            }

            _adapterProcess.Dispose();
        }

        _sessionCts.Dispose();
        _writeLock.Dispose();
        EventConsumerLock.Dispose();
        _logger.LogInformation("[DapSession] Disposed");
    }
}
