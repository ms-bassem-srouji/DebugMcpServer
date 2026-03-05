using System.Text;
using DebugMcpServer.Dap;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DebugMcpServer.Tests.Fakes;

/// <summary>
/// Helper for creating DapSession instances wired to in-memory streams for testing.
/// </summary>
internal static class DapSessionTestHelper
{
    /// <summary>
    /// Creates a DapSession backed by in-memory streams.
    /// Write DAP messages to adapterOutput to simulate adapter responses.
    /// Read from adapterInput to verify what DapSession sent to the adapter.
    /// </summary>
    public static (DapSession Session, Stream AdapterOutput, Stream AdapterInput) Create(
        ILogger? logger = null,
        int maxPendingEvents = 100)
    {
        // adapterOutput: test writes here → DapSession reads from here (simulates adapter stdout)
        var adapterOutput = new BlockingMemoryStream();
        // adapterInput: DapSession writes here → test reads from here (simulates adapter stdin)
        var adapterInput = new MemoryStream();

        var session = new DapSession(
            inputStream: adapterInput,
            outputStream: adapterOutput,
            logger: logger ?? NullLogger.Instance,
            maxPendingEvents: maxPendingEvents);

        return (session, adapterOutput, adapterInput);
    }

    /// <summary>
    /// Formats a JSON string as a proper DAP message with Content-Length header.
    /// </summary>
    public static byte[] FormatDapMessage(string json)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(json);
        var header = $"Content-Length: {bodyBytes.Length}\r\n\r\n";
        var headerBytes = Encoding.UTF8.GetBytes(header);
        var result = new byte[headerBytes.Length + bodyBytes.Length];
        headerBytes.CopyTo(result, 0);
        bodyBytes.CopyTo(result, headerBytes.Length);
        return result;
    }
}

/// <summary>
/// A stream that blocks on Read until data is written, simulating a pipe.
/// </summary>
internal sealed class BlockingMemoryStream : Stream
{
    private readonly SemaphoreSlim _dataAvailable = new(0);
    private readonly Queue<byte[]> _buffers = new();
    private byte[]? _current;
    private int _currentOffset;
    private bool _completed;
    private readonly object _lock = new();

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Write(byte[] buffer, int offset, int count)
    {
        var copy = new byte[count];
        Array.Copy(buffer, offset, copy, 0, count);
        lock (_lock)
        {
            _buffers.Enqueue(copy);
        }
        _dataAvailable.Release();
    }

    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        Write(buffer, offset, count);
        await Task.CompletedTask;
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Write(buffer.ToArray(), 0, buffer.Length);
        await ValueTask.CompletedTask;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return await ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            if (_current != null && _currentOffset < _current.Length)
            {
                var toCopy = Math.Min(buffer.Length, _current.Length - _currentOffset);
                _current.AsMemory(_currentOffset, toCopy).CopyTo(buffer);
                _currentOffset += toCopy;
                if (_currentOffset >= _current.Length)
                    _current = null;
                return toCopy;
            }

            lock (_lock)
            {
                if (_buffers.Count > 0)
                {
                    _current = _buffers.Dequeue();
                    _currentOffset = 0;
                    continue;
                }

                if (_completed)
                    return 0; // EOF
            }

            await _dataAvailable.WaitAsync(cancellationToken);
        }
    }

    /// <summary>Signal EOF — no more data will be written.</summary>
    public void Complete()
    {
        lock (_lock)
        {
            _completed = true;
        }
        _dataAvailable.Release();
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
