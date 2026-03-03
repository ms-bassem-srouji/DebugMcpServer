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
public class ListThreadsToolTests
{
    private static string GetText(JsonNode result) =>
        result["result"]!["content"]![0]!["text"]!.GetValue<string>();

    private static bool IsError(JsonNode result) =>
        result["result"]!["isError"]!.GetValue<bool>();

    [TestMethod]
    public async Task Returns_Threads_With_ActiveThreadId()
    {
        var session = new FakeSession { ActiveThreadId = 1 };
        session.SetupRequest("threads", JsonNode.Parse("""{"threads":[{"id":1,"name":"Main"},{"id":2,"name":"Worker"}]}""")!);
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<ListThreadsTool>>();
        var tool = new ListThreadsTool(registry, logger);

        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        var text = GetText(result);
        text.Should().Contain("threads");
        text.Should().Contain("activeThreadId");
        text.Should().Contain("1");
    }

    [TestMethod]
    public async Task Handles_Empty_Thread_List()
    {
        var session = new FakeSession { ActiveThreadId = 1 };
        session.SetupRequest("threads", JsonNode.Parse("""{"threads":[]}""")!);
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<ListThreadsTool>>();
        var tool = new ListThreadsTool(registry, logger);

        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        var text = GetText(result);
        text.Should().Contain("threads");
    }

    [TestMethod]
    public async Task DAP_Error_Returns_IsError()
    {
        var session = new FakeSession { ActiveThreadId = 1 };
        session.SetupRequestError("threads", "rpc error");
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<ListThreadsTool>>();
        var tool = new ListThreadsTool(registry, logger);

        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
        var text = GetText(result);
        text.Should().Contain("DAP error");
    }
}
