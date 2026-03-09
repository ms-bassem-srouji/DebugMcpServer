using DebugMcpServer.DotnetDump;
using Microsoft.Extensions.Logging.Abstractions;

namespace DebugMcpServer.Tests.Fakes;

internal static class FakeDotnetDumpRegistry
{
    public static DotnetDumpRegistry Empty()
        => new DotnetDumpRegistry(NullLogger<DotnetDumpRegistry>.Instance);
}
