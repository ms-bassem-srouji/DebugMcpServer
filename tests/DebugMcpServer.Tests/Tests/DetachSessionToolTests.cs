using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using DebugMcpServer.Tools;
using DebugMcpServer.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DebugMcpServer.Tests.Tests;

[TestClass]
public class DetachSessionToolTests
{
    private static string GetText(JsonNode result) =>
        result["result"]!["content"]![0]!["text"]!.GetValue<string>();

    private static bool IsError(JsonNode result) =>
        result["result"]!["isError"]!.GetValue<bool>();

    [TestMethod]
    public async Task Sends_Disconnect_And_Disposes_Session()
    {
        var session = new FakeSession();
        session.SetupRequest("disconnect", JsonNode.Parse("""{}""")!);
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<DetachSessionTool>>();
        var tool = new DetachSessionTool(registry, logger);

        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        session.SentRequests.Should().Contain(r => r.Command == "disconnect");
        session.IsDisposed.Should().BeTrue();
        IsError(result).Should().BeFalse();
        var text = GetText(result);
        text.Should().Contain("detached");
    }

    [TestMethod]
    public async Task Session_Not_Found_Returns_Error()
    {
        var registry = FakeSessionRegistry.Empty();
        var logger = Substitute.For<ILogger<DetachSessionTool>>();
        var tool = new DetachSessionTool(registry, logger);

        var args = JsonNode.Parse("""{"sessionId":"unknown"}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
    }

    [TestMethod]
    public async Task Disconnect_Throws_But_Session_Still_Disposed()
    {
        var session = new FakeSession();
        session.SetupRequestError("disconnect", "vsdbg gone");
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<DetachSessionTool>>();
        var tool = new DetachSessionTool(registry, logger);

        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        session.IsDisposed.Should().BeTrue();
        IsError(result).Should().BeFalse();
        var text = GetText(result);
        text.Should().Contain("detached");
    }
}
