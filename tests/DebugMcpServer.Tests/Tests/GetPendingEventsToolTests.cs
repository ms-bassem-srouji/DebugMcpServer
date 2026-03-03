using System.Text.Json;
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
public class GetPendingEventsToolTests
{
    private static string GetText(JsonNode result) =>
        result["result"]!["content"]![0]!["text"]!.GetValue<string>();

    private static GetPendingEventsTool CreateTool(DapSessionRegistry registry)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new DebugOptions
        {
            StepTimeoutSeconds = 1,
            ContinueTimeoutSeconds = 1
        });
        return new GetPendingEventsTool(registry, options, NullLogger<GetPendingEventsTool>.Instance);
    }

    [TestMethod]
    public async Task Returns_Empty_Events_When_No_Events()
    {
        var session = new FakeSession { ActiveThreadId = 1, State = SessionState.Paused };
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var tool = CreateTool(registry);
        var args = JsonNode.Parse("""{"sessionId":"sess1","waitForStopSeconds":0}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var text = GetText(result);
        var parsed = JsonNode.Parse(text)!;
        parsed["eventCount"]!.GetValue<int>().Should().Be(0);
        parsed["events"]!.AsArray().Should().BeEmpty();
    }

    [TestMethod]
    public async Task Returns_Formatted_Stopped_Event()
    {
        var session = new FakeSession { ActiveThreadId = 1, State = SessionState.Paused };
        session.EnqueueEvent(new DapEvent("stopped", JsonNode.Parse("""
            {"reason":"breakpoint","threadId":3,"allThreadsStopped":true}
            """)));
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var tool = CreateTool(registry);
        var args = JsonNode.Parse("""{"sessionId":"sess1","waitForStopSeconds":0}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var text = GetText(result);
        var parsed = JsonNode.Parse(text)!;
        var evt = parsed["events"]![0]!;
        evt["type"]!.GetValue<string>().Should().Be("stopped");
        evt["reason"]!.GetValue<string>().Should().Be("breakpoint");
        evt["threadId"]!.GetValue<int>().Should().Be(3);
    }

    [TestMethod]
    public async Task Returns_Formatted_Output_Event()
    {
        var session = new FakeSession { ActiveThreadId = 1, State = SessionState.Paused };
        session.EnqueueEvent(new DapEvent("output", JsonNode.Parse("""
            {"category":"stdout","output":"Hello World\n"}
            """)));
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var tool = CreateTool(registry);
        var args = JsonNode.Parse("""{"sessionId":"sess1","waitForStopSeconds":0}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var text = GetText(result);
        var parsed = JsonNode.Parse(text)!;
        var evt = parsed["events"]![0]!;
        evt["type"]!.GetValue<string>().Should().Be("output");
        evt["category"]!.GetValue<string>().Should().Be("stdout");
        evt["output"]!.GetValue<string>().Should().Contain("Hello World");
    }

    [TestMethod]
    public async Task Returns_Formatted_Thread_Event()
    {
        var session = new FakeSession { ActiveThreadId = 1, State = SessionState.Paused };
        session.EnqueueEvent(new DapEvent("thread", JsonNode.Parse("""
            {"threadId":5,"reason":"started"}
            """)));
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var tool = CreateTool(registry);
        var args = JsonNode.Parse("""{"sessionId":"sess1","waitForStopSeconds":0}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var text = GetText(result);
        var parsed = JsonNode.Parse(text)!;
        var evt = parsed["events"]![0]!;
        evt["type"]!.GetValue<string>().Should().Be("thread");
        evt["threadId"]!.GetValue<int>().Should().Be(5);
    }

    [TestMethod]
    public async Task Returns_Formatted_Terminated_Event()
    {
        var session = new FakeSession { ActiveThreadId = 1, State = SessionState.Paused };
        session.EnqueueEvent(new DapEvent("terminated", null));
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var tool = CreateTool(registry);
        var args = JsonNode.Parse("""{"sessionId":"sess1","waitForStopSeconds":0}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var text = GetText(result);
        var parsed = JsonNode.Parse(text)!;
        var evt = parsed["events"]![0]!;
        evt["type"]!.GetValue<string>().Should().Be("terminated");
    }

    [TestMethod]
    public async Task Respects_MaxEvents_Limit()
    {
        var session = new FakeSession { ActiveThreadId = 1, State = SessionState.Paused };
        for (int i = 0; i < 5; i++)
        {
            session.EnqueueEvent(new DapEvent("output", JsonNode.Parse("""
                {"category":"stdout","output":"line\n"}
                """)));
        }
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var tool = CreateTool(registry);
        var args = JsonNode.Parse("""{"sessionId":"sess1","maxEvents":3,"waitForStopSeconds":0}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var text = GetText(result);
        var parsed = JsonNode.Parse(text)!;
        parsed["eventCount"]!.GetValue<int>().Should().Be(3);
    }

    [TestMethod]
    public async Task Clamps_MaxEvents_Min_To_1()
    {
        var session = new FakeSession { ActiveThreadId = 1, State = SessionState.Paused };
        session.EnqueueEvent(new DapEvent("output", JsonNode.Parse("""
            {"category":"stdout","output":"line\n"}
            """)));
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var tool = CreateTool(registry);
        var args = JsonNode.Parse("""{"sessionId":"sess1","maxEvents":0,"waitForStopSeconds":0}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var text = GetText(result);
        var parsed = JsonNode.Parse(text)!;
        // maxEvents=0 is clamped to 1, so at most 1 event returned
        parsed["eventCount"]!.GetValue<int>().Should().Be(1);
    }

    [TestMethod]
    public async Task Returns_ProcessState()
    {
        var session = new FakeSession { ActiveThreadId = 1, State = SessionState.Running };
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var tool = CreateTool(registry);
        var args = JsonNode.Parse("""{"sessionId":"sess1","waitForStopSeconds":0}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var text = GetText(result);
        var parsed = JsonNode.Parse(text)!;
        parsed["processState"]!.GetValue<string>().Should().Be("running");
    }

    [TestMethod]
    public async Task WaitForStopSeconds_Zero_NonBlocking()
    {
        var session = new FakeSession { ActiveThreadId = 1, State = SessionState.Running };
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var tool = CreateTool(registry);
        var args = JsonNode.Parse("""{"sessionId":"sess1","waitForStopSeconds":0}""");

        // Should return immediately with empty events
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var text = GetText(result);
        var parsed = JsonNode.Parse(text)!;
        parsed["eventCount"]!.GetValue<int>().Should().Be(0);
    }
}
