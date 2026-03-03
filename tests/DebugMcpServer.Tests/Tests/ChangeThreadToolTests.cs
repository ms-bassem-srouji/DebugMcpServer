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
public class ChangeThreadToolTests
{
    private static string GetText(JsonNode result) =>
        result["result"]!["content"]![0]!["text"]!.GetValue<string>();

    private static bool IsError(JsonNode result) =>
        result["result"]!["isError"]!.GetValue<bool>();

    private static bool HasErrorCode(JsonNode result, int code) =>
        result["error"]?["code"]?.GetValue<int>() == code;

    [TestMethod]
    public async Task Sets_ActiveThreadId_And_Returns_Success()
    {
        var session = new FakeSession { ActiveThreadId = 1 };
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<ChangeThreadTool>>();
        var tool = new ChangeThreadTool(registry, logger);

        var args = JsonNode.Parse("""{"sessionId":"sess1","threadId":5}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        session.ActiveThreadId.Should().Be(5);
        var text = GetText(result);
        text.Should().Contain("activeThreadId");
        text.Should().Contain("5");
        IsError(result).Should().BeFalse();
    }

    [TestMethod]
    public async Task Missing_SessionId_Returns_Error()
    {
        var registry = FakeSessionRegistry.Empty();
        var logger = Substitute.For<ILogger<ChangeThreadTool>>();
        var tool = new ChangeThreadTool(registry, logger);

        var args = JsonNode.Parse("""{"threadId":5}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        HasErrorCode(result, -32602).Should().BeTrue();
    }

    [TestMethod]
    public async Task Missing_ThreadId_Returns_Error()
    {
        var registry = FakeSessionRegistry.Empty();
        var logger = Substitute.For<ILogger<ChangeThreadTool>>();
        var tool = new ChangeThreadTool(registry, logger);

        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        HasErrorCode(result, -32602).Should().BeTrue();
    }

    [TestMethod]
    public async Task Session_Not_Found_Returns_Error()
    {
        var registry = FakeSessionRegistry.Empty();
        var logger = Substitute.For<ILogger<ChangeThreadTool>>();
        var tool = new ChangeThreadTool(registry, logger);

        var args = JsonNode.Parse("""{"sessionId":"unknown","threadId":1}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
    }
}
