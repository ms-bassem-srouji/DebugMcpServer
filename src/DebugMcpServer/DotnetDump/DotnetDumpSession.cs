using Microsoft.Diagnostics.Runtime;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer.DotnetDump;

/// <summary>
/// Wraps a ClrMD DataTarget + ClrRuntime for .NET dump analysis.
/// No external processes, no screen scraping — direct library API.
/// </summary>
internal sealed class DotnetDumpSession : IDisposable
{
    private readonly DataTarget _target;
    private readonly ClrRuntime _runtime;
    private readonly ILogger _logger;
    private bool _disposed;

    public string DumpPath { get; }
    public bool IsRunning => !_disposed;
    public ClrRuntime Runtime => _runtime;
    public DataTarget Target => _target;

    private DotnetDumpSession(DataTarget target, ClrRuntime runtime, string dumpPath, ILogger logger)
    {
        _target = target;
        _runtime = runtime;
        DumpPath = dumpPath;
        _logger = logger;
    }

    public static DotnetDumpSession Open(string dumpPath, ILogger logger)
    {
        logger.LogInformation("[ClrMD] Opening dump: {DumpPath}", dumpPath);

        var target = DataTarget.LoadDump(dumpPath);

        if (target.ClrVersions.Length == 0)
        {
            target.Dispose();
            throw new InvalidOperationException(
                "No .NET runtime found in the dump file. This may not be a .NET process dump, " +
                "or the dump may be corrupted.");
        }

        var clrInfo = target.ClrVersions[0];
        logger.LogInformation("[ClrMD] Found CLR {Version} ({Flavor})", clrInfo.Version, clrInfo.Flavor);

        var runtime = clrInfo.CreateRuntime();
        logger.LogInformation("[ClrMD] Runtime created. Threads={Threads}, AppDomains={Domains}",
            runtime.Threads.Length, runtime.AppDomains.Length);

        return new DotnetDumpSession(target, runtime, dumpPath, logger);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _runtime.Dispose();
        _target.Dispose();
        _logger.LogInformation("[ClrMD] Session disposed for {DumpPath}", DumpPath);
    }
}
