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
public class ContinueExecutionToolTests
{
    private static string GetText(JsonNode result) =>
        result["result"]!["content"]![0]!["text"]!.GetValue<string>();

    private static bool IsError(JsonNode result) =>
        result["result"]!["isError"]!.GetValue<bool>();

    private static ContinueExecutionTool CreateTool(DapSessionRegistry registry)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new DebugOptions
        {
            ContinueTimeoutSeconds = 1,
            StepTimeoutSeconds = 1
        });
        return new ContinueExecutionTool(registry, options, NullLogger<ContinueExecutionTool>.Instance);
    }

    private static FakeSession CreateSessionWithStoppedEvent(int activeThreadId = 1)
    {
        var session = new FakeSession { ActiveThreadId = activeThreadId, State = SessionState.Paused };
        session.SetupRequest("continue", _ => new JsonObject());
        session.SetupRequest("stackTrace", _ => JsonNode.Parse("""
            {"stackFrames":[{"id":1,"name":"Main","source":{"path":"C:/foo.cs"},"line":42,"column":0}]}
            """)!);
        session.EnqueueEvent(new DapEvent("stopped", JsonNode.Parse("""
            {"reason":"breakpoint","threadId":1,"allThreadsStopped":true}
            """)));
        return session;
    }

    [TestMethod]
    public async Task Sends_Continue_With_ActiveThreadId()
    {
        var session = CreateSessionWithStoppedEvent(activeThreadId: 7);
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var tool = CreateTool(registry);
        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");

        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        session.SentRequests.Should().HaveCount(2); // continue + stackTrace
        session.SentRequests[0].Command.Should().Be("continue");
        session.SentRequests[0].Args!["threadId"]!.GetValue<int>().Should().Be(7);
    }

    [TestMethod]
    public async Task Returns_Stopped_Result_On_Breakpoint_Hit()
    {
        var session = CreateSessionWithStoppedEvent();
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var tool = CreateTool(registry);
        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var text = GetText(result);
        text.Should().Contain("stopped");
        text.Should().Contain("breakpoint");
    }

    [TestMethod]
    public async Task Session_Transitions_To_Running()
    {
        var session = CreateSessionWithStoppedEvent();
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var tool = CreateTool(registry);
        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");

        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        // TransitionToRunning() is called, which sets State = Running
        // FakeSession doesn't auto-transition back when stopped event is read
        session.State.Should().Be(SessionState.Running);
        session.SentRequests[0].Command.Should().Be("continue");
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

    [TestMethod]
    public async Task DAP_Error_Returns_IsError()
    {
        var session = new FakeSession { ActiveThreadId = 1, State = SessionState.Paused };
        session.SetupRequestError("continue", "disconnected");
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var tool = CreateTool(registry);
        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("disconnected");
    }
}
