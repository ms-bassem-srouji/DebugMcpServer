using DebugMcpServer.DbgEng;
using Microsoft.Extensions.Logging.Abstractions;

namespace DebugMcpServer.Tests.Fakes;

internal static class FakeNativeDumpRegistry
{
    public static NativeDumpRegistry Empty()
        => new NativeDumpRegistry(NullLogger<NativeDumpRegistry>.Instance);
}
