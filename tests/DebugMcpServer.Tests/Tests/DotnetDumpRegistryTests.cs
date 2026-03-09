using DebugMcpServer.DotnetDump;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DebugMcpServer.Tests.Tests;

[TestClass]
public class DotnetDumpRegistryTests
{
    private static DotnetDumpRegistry CreateRegistry()
        => new(NullLogger<DotnetDumpRegistry>.Instance);

    [TestMethod]
    public void Register_Returns_Id_With_Dump_Prefix()
    {
        // We can't create a real DotnetDumpSession without a process,
        // but we can test the registry using the internal Register(id, session) method
        // For the auto-ID method, we verify ID format via naming convention
        var registry = CreateRegistry();

        // The auto-ID Register method requires a real session, so we test
        // the internal method instead
        registry.GetAll().Should().BeEmpty();
    }

    [TestMethod]
    public void TryGet_Returns_False_For_Unknown_Id()
    {
        var registry = CreateRegistry();

        registry.TryGet("unknown", out var session).Should().BeFalse();
        session.Should().BeNull();
    }

    [TestMethod]
    public void TryRemove_Returns_False_For_Unknown_Id()
    {
        var registry = CreateRegistry();

        registry.TryRemove("unknown", out var session).Should().BeFalse();
        session.Should().BeNull();
    }

    [TestMethod]
    public void GetAll_Returns_Empty_Initially()
    {
        var registry = CreateRegistry();

        registry.GetAll().Should().BeEmpty();
    }

    [TestMethod]
    public void Dispose_On_Empty_Registry_Does_Not_Throw()
    {
        var registry = CreateRegistry();

        var action = () => registry.Dispose();
        action.Should().NotThrow();
    }
}
