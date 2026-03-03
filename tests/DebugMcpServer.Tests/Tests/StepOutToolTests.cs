using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using DebugMcpServer.Options;
using DebugMcpServer.Tests.Fakes;
using DebugMcpServer.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DebugMcpServer.Tests.Tests;

[TestClass]
public class StepOutToolTests
{
    private static string GetText(JsonNode result) =>
        result["result"]!["content"]![0]!["text"]!.GetValue<string>();

    private static bool IsError(JsonNode result) =>
        result["result"]!["isError"]!.GetValue<bool>();

    private static StepOutTool CreateTool(DapSessionRegistry registry)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new DebugOptions
        {
            StepTimeoutSeconds = 1,
            ContinueTimeoutSeconds = 1
        });
        return new StepOutTool(registry, options, NullLogger<StepOutTool>.Instance);
    }

    private static FakeSession CreatePausedSessionWithStoppedEvent()
    {
        var session = new FakeSession { ActiveThreadId = 1, State = SessionState.Paused };
        session.SetupRequest("stepOut", _ => new JsonObject());
        session.SetupRequest("stackTrace", _ => JsonNode.Parse("""
            {"stackFrames":[{"id":1,"name":"Main","source":{"path":"C:/foo.cs"},"line":42,"column":0}]}
            """)!);
        session.EnqueueEvent(new DapEvent("stopped", JsonNode.Parse("""
            {"reason":"step","threadId":1,"allThreadsStopped":true}
            """)));
        return session;
    }

    [TestMethod]
    public async Task Returns_SessionNotPaused_When_Running()
    {
        var session = new FakeSession { ActiveThreadId = 1, State = SessionState.Running };
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var tool = CreateTool(registry);
        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("running");
    }

    [TestMethod]
    public async Task Sends_StepOut_Command()
    {
        var session = CreatePausedSessionWithStoppedEvent();
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var tool = CreateTool(registry);
        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");

        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        session.SentRequests[0].Command.Should().Be("stepOut");
    }

    [TestMethod]
    public async Task Returns_Stopped_Result()
    {
        var session = CreatePausedSessionWithStoppedEvent();
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var tool = CreateTool(registry);
        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var text = GetText(result);
        text.Should().Contain("stopped");
    }

    [TestMethod]
    public async Task Session_Not_Found_Returns_Error()
    {
        var registry = FakeSessionRegistry.Empty();
        var tool = CreateTool(registry);
        var args = JsonNode.Parse("""{"sessionId":"unknown"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("unknown");
    }
}
