using DebugMcpServer.Dap;
using DebugMcpServer.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DebugMcpServer.Tests.Tests;

[TestClass]
public class DapSessionRegistryTests
{
    private static DapSessionRegistry CreateRegistry() =>
        new DapSessionRegistry(NullLogger<DapSessionRegistry>.Instance);

    [TestMethod]
    public void Register_StoresSessionAndReturnsNonEmptyId()
    {
        var registry = CreateRegistry();
        var session = new FakeSession();

        var id = registry.Register(session);

        id.Should().NotBeNullOrEmpty();
        registry.TryGet(id, out var retrieved).Should().BeTrue();
        retrieved.Should().BeSameAs(session);
    }

    [TestMethod]
    public void Register_EachCallGeneratesUniqueId()
    {
        var registry = CreateRegistry();
        var id1 = registry.Register(new FakeSession());
        var id2 = registry.Register(new FakeSession());
        id1.Should().NotBe(id2);
    }

    [TestMethod]
    public void TryGet_MissingId_ReturnsFalse()
    {
        var registry = CreateRegistry();
        registry.TryGet("nonexistent", out var session).Should().BeFalse();
        session.Should().BeNull();
    }

    [TestMethod]
    public void TryRemove_RemovesAndReturnsSameInstance()
    {
        var registry = CreateRegistry();
        var fake = new FakeSession();
        var id = registry.Register(fake);

        var removed = registry.TryRemove(id, out var session);

        removed.Should().BeTrue();
        session.Should().BeSameAs(fake);
        registry.TryGet(id, out _).Should().BeFalse();
    }

    [TestMethod]
    public void TryRemove_NonExistentId_ReturnsFalse()
    {
        var registry = CreateRegistry();
        registry.TryRemove("nope", out _).Should().BeFalse();
    }

    [TestMethod]
    public void Dispose_DisposesAllSessions()
    {
        var registry = CreateRegistry();
        var s1 = new FakeSession();
        var s2 = new FakeSession();
        registry.Register(s1);
        registry.Register(s2);

        registry.Dispose();

        s1.IsDisposed.Should().BeTrue();
        s2.IsDisposed.Should().BeTrue();
    }

    [TestMethod]
    public void ConcurrentRegistrations_AreAllRetrievable()
    {
        var registry = CreateRegistry();
        var ids = new System.Collections.Concurrent.ConcurrentBag<string>();

        Parallel.For(0, 20, _ =>
        {
            var id = registry.Register(new FakeSession());
            ids.Add(id);
        });

        foreach (var id in ids)
            registry.TryGet(id, out _).Should().BeTrue();
    }
}
