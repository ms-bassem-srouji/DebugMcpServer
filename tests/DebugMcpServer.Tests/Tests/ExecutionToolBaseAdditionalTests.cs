using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using DebugMcpServer.Tests.Fakes;
using DebugMcpServer.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DebugMcpServer.Tests.Tests;

[TestClass]
public class ExecutionToolBaseAdditionalTests
{
    private sealed class TestExecutionBase : ExecutionToolBase
    {
        public static Task<string> TestGetLocation(IDapSession session, int threadId, CancellationToken ct)
            => GetTopFrameLocationAsync(session, threadId, ct);

        public static Task<JsonNode> TestWaitForStopped(IDapSession session, JsonNode? id, int timeout, ILogger logger, CancellationToken ct)
            => WaitForStoppedResultAsync(session, id, timeout, logger, ct);
    }

    private static string GetText(JsonNode result) =>
        result["result"]!["content"]![0]!["text"]!.GetValue<string>();

    private static bool IsError(JsonNode result) =>
        result["result"]!["isError"]!.GetValue<bool>();

    [TestMethod]
    public async Task WaitForStopped_TerminatedEvent_ReturnsTerminated()
    {
        var session = new FakeSession { ActiveThreadId = 1, State = SessionState.Paused };
        session.EnqueueEvent(new DapEvent("terminated", JsonNode.Parse("{}")));
        var logger = Substitute.For<ILogger>();

        var result = await TestExecutionBase.TestWaitForStopped(session, JsonValue.Create(1), 5, logger, CancellationToken.None);

        var text = GetText(result);
        text.Should().Contain("terminated");
        text.Should().Contain("exited");
    }

    [TestMethod]
    public async Task WaitForStopped_BuffersNonStoppedEvents_BeforeStopped()
    {
        var session = new FakeSession { ActiveThreadId = 1, State = SessionState.Paused };
        session.SetupRequest("stackTrace", _ => JsonNode.Parse("""
            {"stackFrames":[{"id":1,"name":"Main","source":{"path":"test.cs"},"line":1,"column":0}]}
            """)!);

        // Enqueue output events before the stopped event
        session.EnqueueEvent(new DapEvent("output", JsonNode.Parse("""{"output":"debug log"}""")));
        session.EnqueueEvent(new DapEvent("thread", JsonNode.Parse("""{"reason":"started","threadId":2}""")));
        session.EnqueueEvent(new DapEvent("stopped", JsonNode.Parse("""{"reason":"step","threadId":1,"allThreadsStopped":true}""")));

        var logger = Substitute.For<ILogger>();

        var result = await TestExecutionBase.TestWaitForStopped(session, JsonValue.Create(1), 5, logger, CancellationToken.None);

        var text = GetText(result);
        text.Should().Contain("\"outcome\":\"stopped\"");
        text.Should().Contain("step");
    }

    [TestMethod]
    public async Task WaitForStopped_StoppedWithDescription_IncludesDescription()
    {
        var session = new FakeSession { ActiveThreadId = 1, State = SessionState.Paused };
        session.SetupRequest("stackTrace", _ => JsonNode.Parse("""
            {"stackFrames":[{"id":1,"name":"Main","source":{"path":"test.cs"},"line":1,"column":0}]}
            """)!);
        session.EnqueueEvent(new DapEvent("stopped", JsonNode.Parse(
            """{"reason":"exception","description":"NullReferenceException","threadId":1,"allThreadsStopped":true}""")));

        var logger = Substitute.For<ILogger>();

        var result = await TestExecutionBase.TestWaitForStopped(session, JsonValue.Create(1), 5, logger, CancellationToken.None);

        var text = GetText(result);
        text.Should().Contain("NullReferenceException");
        text.Should().Contain("exception");
    }

    [TestMethod]
    public async Task GetTopFrameLocation_MissingSourcePath_FallsBackToName()
    {
        var session = new FakeSession { ActiveThreadId = 1, State = SessionState.Paused };
        session.SetupRequest("stackTrace", _ => JsonNode.Parse("""
            {"stackFrames":[{"id":1,"name":"Main","source":{"name":"Module.dll"},"line":10,"column":1}]}
            """)!);

        var result = await TestExecutionBase.TestGetLocation(session, 1, CancellationToken.None);

        var node = JsonNode.Parse(result)!;
        node["source"]!.GetValue<string>().Should().Be("Module.dll");
    }

    [TestMethod]
    public async Task GetTopFrameLocation_NullFrame_ReturnsNull()
    {
        var session = new FakeSession { ActiveThreadId = 1, State = SessionState.Paused };
        session.SetupRequest("stackTrace", _ => JsonNode.Parse("""{"stackFrames":[]}""")!);

        var result = await TestExecutionBase.TestGetLocation(session, 1, CancellationToken.None);

        result.Should().Be("null");
    }

    [TestMethod]
    public async Task GetTopFrameLocation_MissingFields_ReturnsDefaults()
    {
        var session = new FakeSession { ActiveThreadId = 1, State = SessionState.Paused };
        session.SetupRequest("stackTrace", _ => JsonNode.Parse("""
            {"stackFrames":[{"id":1,"name":"Main"}]}
            """)!);

        var result = await TestExecutionBase.TestGetLocation(session, 1, CancellationToken.None);

        var node = JsonNode.Parse(result)!;
        node["source"]!.GetValue<string>().Should().Be("unknown");
        node["line"]!.GetValue<int>().Should().Be(0);
        node["column"]!.GetValue<int>().Should().Be(0);
    }

    [TestMethod]
    public async Task WaitForStopped_StoppedEvent_UsesActiveThreadId_WhenBodyHasNoThreadId()
    {
        var session = new FakeSession { ActiveThreadId = 7, State = SessionState.Paused };
        session.SetupRequest("stackTrace", _ => JsonNode.Parse("""
            {"stackFrames":[{"id":1,"name":"Main","source":{"path":"test.cs"},"line":1,"column":0}]}
            """)!);
        session.EnqueueEvent(new DapEvent("stopped", JsonNode.Parse(
            """{"reason":"breakpoint","allThreadsStopped":true}""")));

        var logger = Substitute.For<ILogger>();

        var result = await TestExecutionBase.TestWaitForStopped(session, JsonValue.Create(1), 5, logger, CancellationToken.None);

        var text = GetText(result);
        var parsed = JsonNode.Parse(text)!;
        parsed["threadId"]!.GetValue<int>().Should().Be(7);
    }
}
