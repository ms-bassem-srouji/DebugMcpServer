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
public class GetCallStackToolTests
{
    private static string GetText(JsonNode result) =>
        result["result"]!["content"]![0]!["text"]!.GetValue<string>();

    private static bool IsError(JsonNode result) =>
        result["result"]!["isError"]!.GetValue<bool>();

    private static (GetCallStackTool tool, FakeSession session) CreateTool(JsonNode? stackTraceResponse = null)
    {
        var session = new FakeSession { ActiveThreadId = 1 };
        session.SetupRequest("stackTrace", stackTraceResponse ?? JsonNode.Parse("""
        {
            "stackFrames": [
                {"id":1,"name":"Main","line":10,"column":1,"source":{"path":"C:\\app\\Program.cs","name":"Program.cs"}},
                {"id":2,"name":"Run","line":25,"column":5,"source":{"path":"C:\\app\\App.cs","name":"App.cs"}}
            ],
            "totalFrames": 2
        }
        """)!);
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<GetCallStackTool>>();
        return (new GetCallStackTool(registry, logger), session);
    }

    [TestMethod]
    public async Task Returns_Frames_Array()
    {
        var (tool, _) = CreateTool();

        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        var text = GetText(result);
        text.Should().Contain("frames");
        text.Should().Contain("Main");
        text.Should().Contain("Run");
    }

    [TestMethod]
    public async Task Clamps_Levels_To_Maximum_100()
    {
        var (tool, session) = CreateTool();

        var args = JsonNode.Parse("""{"sessionId":"sess1","levels":200}""");
        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        session.SentRequests.Should().HaveCountGreaterThanOrEqualTo(1);
        var sentArgs = session.SentRequests[0].Args;
        sentArgs!["levels"]!.GetValue<int>().Should().Be(100);
    }

    [TestMethod]
    public async Task Uses_ActiveThreadId()
    {
        var (tool, session) = CreateTool();
        session.ActiveThreadId = 7;

        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");
        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var sentArgs = session.SentRequests[0].Args;
        sentArgs!["threadId"]!.GetValue<int>().Should().Be(7);
    }

    [TestMethod]
    public async Task Returns_TotalFrames()
    {
        var response = JsonNode.Parse("""
        {
            "stackFrames": [{"id":1,"name":"Main","line":10,"column":1}],
            "totalFrames": 50
        }
        """)!;
        var (tool, _) = CreateTool(response);

        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var text = GetText(result);
        text.Should().Contain("50");
        text.Should().Contain("totalFrames");
    }

    [TestMethod]
    public async Task DAP_Error_Returns_IsError()
    {
        var session = new FakeSession { ActiveThreadId = 1 };
        session.SetupRequestError("stackTrace", "thread not suspended");
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<GetCallStackTool>>();
        var tool = new GetCallStackTool(registry, logger);

        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
        var text = GetText(result);
        text.Should().Contain("thread not suspended");
    }
}
