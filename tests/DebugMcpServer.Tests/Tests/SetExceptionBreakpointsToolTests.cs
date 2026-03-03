using System.Text.Json.Nodes;
using DebugMcpServer.Tools;
using DebugMcpServer.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DebugMcpServer.Tests.Tests;

[TestClass]
public class SetExceptionBreakpointsToolTests
{
    private static string GetText(JsonNode result) =>
        result["result"]!["content"]![0]!["text"]!.GetValue<string>();

    private static bool IsError(JsonNode result) =>
        result["result"]!["isError"]!.GetValue<bool>();

    private static bool HasErrorCode(JsonNode result, int code) =>
        result["error"]?["code"]?.GetValue<int>() == code;

    private static (SetExceptionBreakpointsTool tool, FakeSession session) CreateTool()
    {
        var session = new FakeSession();
        session.SetupRequest("setExceptionBreakpoints", JsonNode.Parse("""
            {
                "breakpoints": [
                    {"verified": true, "id": 1},
                    {"verified": true, "id": 2}
                ]
            }
            """)!);
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<SetExceptionBreakpointsTool>>();
        return (new SetExceptionBreakpointsTool(registry, logger), session);
    }

    [TestMethod]
    public async Task Sends_SetExceptionBreakpoints_Dap_Request()
    {
        var (tool, session) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1","filters":["all","unhandled"]}""");

        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        session.SentRequests.Should().Contain(r => r.Command == "setExceptionBreakpoints");
    }

    [TestMethod]
    public async Task Sends_Filters_In_Payload()
    {
        var (tool, session) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1","filters":["all","unhandled"]}""");

        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var req = session.SentRequests.First(r => r.Command == "setExceptionBreakpoints");
        var filters = req.Args!["filters"] as JsonArray;
        filters.Should().NotBeNull();
        filters!.Select(f => f!.GetValue<string>()).Should().BeEquivalentTo("all", "unhandled");
    }

    [TestMethod]
    public async Task Returns_Confirmed_Filters_And_Breakpoints()
    {
        var (tool, _) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1","filters":["all","unhandled"]}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        var json = JsonNode.Parse(GetText(result))!;
        var filters = (json["filters"] as JsonArray)!;
        filters.Select(f => f!.GetValue<string>()).Should().BeEquivalentTo("all", "unhandled");

        var breakpoints = (json["breakpoints"] as JsonArray)!;
        breakpoints.Should().HaveCount(2);
        breakpoints[0]!["verified"]!.GetValue<bool>().Should().BeTrue();
        breakpoints[0]!["id"]!.GetValue<int>().Should().Be(1);
    }

    [TestMethod]
    public async Task Empty_Filters_Clears_Exception_Breakpoints()
    {
        var (tool, session) = CreateTool();
        session.SetupRequest("setExceptionBreakpoints", JsonNode.Parse("""{"breakpoints":[]}""")!);
        var args = JsonNode.Parse("""{"sessionId":"sess1","filters":[]}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        var req = session.SentRequests.First(r => r.Command == "setExceptionBreakpoints");
        var filters = req.Args!["filters"] as JsonArray;
        filters.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Response_Without_Breakpoints_Array_Omits_It()
    {
        var session = new FakeSession();
        session.SetupRequest("setExceptionBreakpoints", JsonNode.Parse("""{}""")!);
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<SetExceptionBreakpointsTool>>();
        var tool = new SetExceptionBreakpointsTool(registry, logger);
        var args = JsonNode.Parse("""{"sessionId":"sess1","filters":["thrown"]}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        var json = JsonNode.Parse(GetText(result))!;
        json["filters"].Should().NotBeNull();
        json["breakpoints"].Should().BeNull();
    }

    [TestMethod]
    public async Task Missing_SessionId_Returns_Error()
    {
        var (tool, _) = CreateTool();
        var args = JsonNode.Parse("""{"filters":["all"]}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        HasErrorCode(result, -32602).Should().BeTrue();
    }

    [TestMethod]
    public async Task Missing_Filters_Returns_Error()
    {
        var (tool, _) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        HasErrorCode(result, -32602).Should().BeTrue();
    }

    [TestMethod]
    public async Task Session_Not_Found_Returns_Error()
    {
        var registry = FakeSessionRegistry.Empty();
        var logger = Substitute.For<ILogger<SetExceptionBreakpointsTool>>();
        var tool = new SetExceptionBreakpointsTool(registry, logger);
        var args = JsonNode.Parse("""{"sessionId":"unknown","filters":["all"]}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("not found");
    }

    [TestMethod]
    public async Task Dap_Error_Returns_Error_Result()
    {
        var session = new FakeSession();
        session.SetupRequestError("setExceptionBreakpoints", "Not supported");
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<SetExceptionBreakpointsTool>>();
        var tool = new SetExceptionBreakpointsTool(registry, logger);
        var args = JsonNode.Parse("""{"sessionId":"sess1","filters":["all"]}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("DAP error");
    }

    [TestMethod]
    public async Task Does_Not_Require_Paused_State()
    {
        var (tool, session) = CreateTool();
        session.State = DebugMcpServer.Dap.SessionState.Running;
        var args = JsonNode.Parse("""{"sessionId":"sess1","filters":["all"]}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
    }
}
