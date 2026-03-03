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
public class ExecutionToolBaseTests
{
    private sealed class TestExecutionBase : ExecutionToolBase
    {
        public static Task<string> TestGetLocation(IDapSession session, int threadId, CancellationToken ct)
            => GetTopFrameLocationAsync(session, threadId, ct);

        public static JsonNode TestNotFound(JsonNode? id, string sid)
            => SessionNotFound(id, sid);

        public static JsonNode TestNotPaused(JsonNode? id)
            => SessionNotPaused(id);

        public static Task<JsonNode> TestWaitForStopped(IDapSession session, JsonNode? id, int timeout, ILogger logger, CancellationToken ct)
            => WaitForStoppedResultAsync(session, id, timeout, logger, ct);
    }

    private static string GetText(JsonNode result) =>
        result["result"]!["content"]![0]!["text"]!.GetValue<string>();

    private static bool IsError(JsonNode result) =>
        result["result"]!["isError"]!.GetValue<bool>();

    [TestMethod]
    public async Task GetTopFrameLocationAsync_ReturnsCorrectJson()
    {
        var session = new FakeSession { ActiveThreadId = 1, State = SessionState.Paused };
        session.SetupRequest("stackTrace", _ => JsonNode.Parse("""
            {"stackFrames":[{"id":1,"name":"Main","source":{"path":"C:/foo.cs"},"line":42,"column":5}]}
            """)!);

        var result = await TestExecutionBase.TestGetLocation(session, 1, CancellationToken.None);

        var node = JsonNode.Parse(result)!;
        node["source"]!.GetValue<string>().Should().Be("C:/foo.cs");
        node["line"]!.GetValue<int>().Should().Be(42);
        node["column"]!.GetValue<int>().Should().Be(5);
    }

    [TestMethod]
    public async Task GetTopFrameLocationAsync_ReturnsNull_OnException()
    {
        var session = new FakeSession { ActiveThreadId = 1, State = SessionState.Paused };
        session.SetupRequestError("stackTrace", "something went wrong");

        var result = await TestExecutionBase.TestGetLocation(session, 1, CancellationToken.None);

        result.Should().Be("null");
    }

    [TestMethod]
    public void SessionNotFound_ReturnsIsError_With_SessionId()
    {
        var result = TestExecutionBase.TestNotFound(JsonValue.Create(1), "sess-abc");

        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("sess-abc");
    }

    [TestMethod]
    public void SessionNotPaused_ReturnsIsError()
    {
        var result = TestExecutionBase.TestNotPaused(JsonValue.Create(1));

        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("running");
    }

    [TestMethod]
    public async Task WaitForStoppedResultAsync_Returns_Stopped_On_Stopped_Event()
    {
        var session = new FakeSession { ActiveThreadId = 1, State = SessionState.Paused };
        session.SetupRequest("stackTrace", _ => JsonNode.Parse("""
            {"stackFrames":[{"id":1,"name":"Main","source":{"path":"C:/foo.cs"},"line":42,"column":0}]}
            """)!);
        session.EnqueueEvent(new DapEvent("stopped", JsonNode.Parse("""
            {"reason":"breakpoint","threadId":1,"allThreadsStopped":true}
            """)));
        var logger = Substitute.For<ILogger>();

        var result = await TestExecutionBase.TestWaitForStopped(session, JsonValue.Create(1), 5, logger, CancellationToken.None);

        var text = GetText(result);
        text.Should().Contain("\"outcome\":\"stopped\"");
        text.Should().Contain("breakpoint");
    }

    [TestMethod]
    public async Task WaitForStoppedResultAsync_Returns_Terminated_When_Channel_Closes()
    {
        var session = new FakeSession { ActiveThreadId = 1, State = SessionState.Paused };
        session.CompleteEventChannel();
        var logger = Substitute.For<ILogger>();

        var result = await TestExecutionBase.TestWaitForStopped(session, JsonValue.Create(1), 5, logger, CancellationToken.None);

        var text = GetText(result);
        text.Should().Contain("terminated");
        IsError(result).Should().BeTrue();
    }

    [TestMethod]
    public async Task WaitForStoppedResultAsync_Returns_Running_On_Timeout()
    {
        var session = new FakeSession { ActiveThreadId = 1, State = SessionState.Paused };
        // No events enqueued, channel stays open → timeout
        var logger = Substitute.For<ILogger>();

        var result = await TestExecutionBase.TestWaitForStopped(session, JsonValue.Create(1), 1, logger, CancellationToken.None);

        var text = GetText(result);
        var parsed = System.Text.Json.Nodes.JsonNode.Parse(text)!;
        parsed["outcome"]!.GetValue<string>().Should().Be("running");
    }
}
