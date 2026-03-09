using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using static DebugMcpServer.DbgEng.DbgEngNative;

namespace DebugMcpServer.DbgEng;

/// <summary>
/// Wraps Windows DbgEng COM objects for native dump analysis.
/// All COM operations run on a dedicated STA thread to ensure thread affinity.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class DbgEngSession : IDisposable
{
    private readonly Thread _thread;
    private readonly BlockingCollection<(string Command, TaskCompletionSource<string> Tcs)> _commandQueue = new();
    private readonly ILogger _logger;
    private bool _disposed;

    public string DumpPath { get; }
    public bool IsRunning => !_disposed;

    private DbgEngSession(Thread thread, string dumpPath, ILogger logger)
    {
        _thread = thread;
        DumpPath = dumpPath;
        _logger = logger;
    }

    public static DbgEngSession Open(string dumpPath, ILogger logger)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "Native dump analysis via DbgEng is only available on Windows. " +
                "On Linux/macOS, use load_dump_file with the 'cpp' or 'lldb' adapter instead.");

        logger.LogInformation("[DbgEng] Opening dump: {DumpPath}", dumpPath);

        var readyTcs = new TaskCompletionSource<DbgEngSession>();

        var thread = new Thread(() =>
        {
            IDebugClient? client = null;
            IDebugControl? control = null;
            DbgEngOutputCapture? capture = null;

            try
            {
                var iid = IID_IDebugClient;
                int hr = DebugCreate(ref iid, out var clientPtr);
                if (hr < 0) { readyTcs.SetException(new COMException($"DebugCreate failed", hr)); return; }

                client = (IDebugClient)Marshal.GetObjectForIUnknown(clientPtr);
                Marshal.Release(clientPtr);

                capture = new DbgEngOutputCapture();
                client.SetOutputCallbacks(capture.ComPointer);

                client.OpenDumpFile(dumpPath);
                logger.LogInformation("[DbgEng] OpenDumpFile succeeded");

                control = (IDebugControl)client;
                hr = control.WaitForEvent(0, INFINITE);
                logger.LogInformation("[DbgEng] WaitForEvent: 0x{HR:X8}", hr);

                capture.GetOutput(); // drain initial output

                var session = new DbgEngSession(Thread.CurrentThread, dumpPath, logger);
                readyTcs.SetResult(session);

                // Command processing loop — runs on this STA thread
                foreach (var (command, tcs) in session._commandQueue.GetConsumingEnumerable())
                {
                    try
                    {
                        capture.GetOutput(); // clear
                        hr = control.Execute(DEBUG_OUTCTL_THIS_CLIENT, command, DEBUG_EXECUTE_DEFAULT);
                        var output = capture.GetOutput();

                        if (hr < 0 && string.IsNullOrWhiteSpace(output))
                            output = $"Command failed with HRESULT 0x{hr:X8}";

                        tcs.SetResult(output);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetResult($"Error: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                if (!readyTcs.Task.IsCompleted)
                    readyTcs.SetException(ex);
            }
            finally
            {
                try { client?.EndSession(DEBUG_END_ACTIVE_DETACH); } catch { }
                if (client != null) Marshal.ReleaseComObject(client);
                capture?.Dispose();
                logger.LogInformation("[DbgEng] Thread exiting");
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Name = $"DbgEng-{Path.GetFileName(dumpPath)}";
        thread.Start();

        // Wait for the session to be ready
        if (!readyTcs.Task.Wait(TimeSpan.FromSeconds(30)))
            throw new TimeoutException("DbgEng failed to open dump within 30 seconds");

        return readyTcs.Task.Result;
    }

    public string ExecuteCommand(string command)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var tcs = new TaskCompletionSource<string>();
        _commandQueue.Add((command, tcs));

        if (!tcs.Task.Wait(TimeSpan.FromSeconds(30)))
            return $"Command '{command}' timed out after 30 seconds";

        return tcs.Task.Result;
    }

    public uint GetThreadCount()
    {
        var output = ExecuteCommand("~");
        // Count lines that look like thread entries
        if (string.IsNullOrWhiteSpace(output) || output.StartsWith("Command failed"))
            return 0;
        return (uint)output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Count(l => l.TrimStart().StartsWith('.') || l.TrimStart().StartsWith('#') || char.IsDigit(l.TrimStart().FirstOrDefault()));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _commandQueue.CompleteAdding();
        _thread.Join(5000);
        _commandQueue.Dispose();

        _logger.LogInformation("[DbgEng] Session disposed for {DumpPath}", DumpPath);
    }
}
