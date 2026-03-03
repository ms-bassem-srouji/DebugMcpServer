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
public class SetFunctionBreakpointsToolTests
{
    private static string GetText(JsonNode result) =>
        result["result"]!["content"]![0]!["text"]!.GetValue<string>();

    private static bool IsError(JsonNode result) =>
        result["result"]!["isError"]!.GetValue<bool>();

    private static bool HasErrorCode(JsonNode result, int code) =>
        result["error"]?["code"]?.GetValue<int>() == code;

    private static (SetFunctionBreakpointsTool tool, FakeSession session, DapSessionRegistry registry) CreateTool()
    {
        var session = new FakeSession();
        session.SetupRequest("setFunctionBreakpoints", JsonNode.Parse("""
            {"breakpoints":[{"id":1,"verified":true,"line":10,"message":null}]}
            """)!);
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<SetFunctionBreakpointsTool>>();
        return (new SetFunctionBreakpointsTool(registry, logger), session, registry);
    }

    [TestMethod]
    public async Task Sends_SetFunctionBreakpoints_Request()
    {
        var (tool, session, _) = CreateTool();

        var args = JsonNode.Parse("""{"sessionId":"sess1","breakpoints":[{"name":"MyClass.MyMethod"}]}""");
        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        session.SentRequests.Should().Contain(r => r.Command == "setFunctionBreakpoints");
    }

    [TestMethod]
    public async Task Sends_Function_Name_In_Payload()
    {
        var (tool, session, _) = CreateTool();

        var args = JsonNode.Parse("""{"sessionId":"sess1","breakpoints":[{"name":"Program.Main"}]}""");
        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var req = session.SentRequests.First(r => r.Command == "setFunctionBreakpoints");
        var bps = req.Args!["breakpoints"] as JsonArray;
        bps.Should().NotBeNull();
        bps![0]!["name"]!.GetValue<string>().Should().Be("Program.Main");
    }

    [TestMethod]
    public async Task Sends_Condition_And_HitCondition_In_Payload()
    {
        var (tool, session, _) = CreateTool();

        var args = JsonNode.Parse("""{"sessionId":"sess1","breakpoints":[{"name":"Foo.Bar","condition":"x > 0","hitCondition":"3"}]}""");
        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var req = session.SentRequests.First(r => r.Command == "setFunctionBreakpoints");
        var bp = (req.Args!["breakpoints"] as JsonArray)![0]!;
        bp["name"]!.GetValue<string>().Should().Be("Foo.Bar");
        bp["condition"]!.GetValue<string>().Should().Be("x > 0");
        bp["hitCondition"]!.GetValue<string>().Should().Be("3");
    }

    [TestMethod]
    public async Task Returns_Verified_Breakpoints()
    {
        var (tool, _, _) = CreateTool();

        var args = JsonNode.Parse("""{"sessionId":"sess1","breakpoints":[{"name":"MyClass.MyMethod"}]}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        var text = GetText(result);
        text.Should().Contain("breakpoints");
        text.Should().Contain("\"verified\":true");
    }

    [TestMethod]
    public async Task Sends_Multiple_Function_Breakpoints()
    {
        var session = new FakeSession();
        session.SetupRequest("setFunctionBreakpoints", JsonNode.Parse("""
            {"breakpoints":[{"id":1,"verified":true,"line":10},{"id":2,"verified":true,"line":20}]}
            """)!);
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<SetFunctionBreakpointsTool>>();
        var tool = new SetFunctionBreakpointsTool(registry, logger);

        var args = JsonNode.Parse("""{"sessionId":"sess1","breakpoints":[{"name":"Foo.Bar"},{"name":"Baz.Qux"}]}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        var req = session.SentRequests.First(r => r.Command == "setFunctionBreakpoints");
        var bps = req.Args!["breakpoints"] as JsonArray;
        bps.Should().HaveCount(2);
        bps![0]!["name"]!.GetValue<string>().Should().Be("Foo.Bar");
        bps![1]!["name"]!.GetValue<string>().Should().Be("Baz.Qux");
    }

    [TestMethod]
    public async Task Missing_SessionId_Returns_Error()
    {
        var (tool, _, _) = CreateTool();

        var args = JsonNode.Parse("""{"breakpoints":[{"name":"Foo.Bar"}]}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        HasErrorCode(result, -32602).Should().BeTrue();
    }

    [TestMethod]
    public async Task Missing_Breakpoints_Returns_Error()
    {
        var (tool, _, _) = CreateTool();

        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        HasErrorCode(result, -32602).Should().BeTrue();
    }

    [TestMethod]
    public async Task Empty_Breakpoints_Array_Returns_Error()
    {
        var (tool, _, _) = CreateTool();

        var args = JsonNode.Parse("""{"sessionId":"sess1","breakpoints":[]}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        HasErrorCode(result, -32602).Should().BeTrue();
    }

    [TestMethod]
    public async Task Breakpoint_Missing_Name_Returns_Error()
    {
        var (tool, _, _) = CreateTool();

        var args = JsonNode.Parse("""{"sessionId":"sess1","breakpoints":[{"condition":"x > 0"}]}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        HasErrorCode(result, -32602).Should().BeTrue();
    }

    [TestMethod]
    public async Task Session_Not_Found_Returns_Error()
    {
        var registry = FakeSessionRegistry.Empty();
        var logger = Substitute.For<ILogger<SetFunctionBreakpointsTool>>();
        var tool = new SetFunctionBreakpointsTool(registry, logger);

        var args = JsonNode.Parse("""{"sessionId":"unknown","breakpoints":[{"name":"Foo.Bar"}]}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
    }

    [TestMethod]
    public async Task Dap_Error_Returns_IsError_True()
    {
        var session = new FakeSession();
        session.SetupRequestError("setFunctionBreakpoints", "Function not found");
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<SetFunctionBreakpointsTool>>();
        var tool = new SetFunctionBreakpointsTool(registry, logger);

        var args = JsonNode.Parse("""{"sessionId":"sess1","breakpoints":[{"name":"NonExistent"}]}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("DAP error");
    }

    [TestMethod]
    public async Task Omits_Condition_From_Payload_When_Not_Provided()
    {
        var (tool, session, _) = CreateTool();

        var args = JsonNode.Parse("""{"sessionId":"sess1","breakpoints":[{"name":"Foo.Bar"}]}""");
        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var req = session.SentRequests.First(r => r.Command == "setFunctionBreakpoints");
        var bp = (req.Args!["breakpoints"] as JsonArray)![0]!;
        bp["condition"].Should().BeNull();
        bp["hitCondition"].Should().BeNull();
    }
}
