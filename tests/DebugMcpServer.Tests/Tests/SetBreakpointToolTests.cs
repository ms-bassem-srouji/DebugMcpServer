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
public class SetBreakpointToolTests
{
    private static string GetText(JsonNode result) =>
        result["result"]!["content"]![0]!["text"]!.GetValue<string>();

    private static bool IsError(JsonNode result) =>
        result["result"]!["isError"]!.GetValue<bool>();

    private static bool HasErrorCode(JsonNode result, int code) =>
        result["error"]?["code"]?.GetValue<int>() == code;

    private static (SetBreakpointTool tool, FakeSession session, DapSessionRegistry registry) CreateTool()
    {
        var session = new FakeSession();
        session.SetupRequest("setBreakpoints", JsonNode.Parse("""{"breakpoints":[{"id":1,"verified":true,"line":42}]}""")!);
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<SetBreakpointTool>>();
        return (new SetBreakpointTool(registry, logger), session, registry);
    }

    [TestMethod]
    public async Task Adds_Breakpoint_To_Session_Dict()
    {
        var (tool, session, _) = CreateTool();

        var args = JsonNode.Parse("""{"sessionId":"sess1","file":"C:\\app\\Program.cs","line":42}""");
        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        session.Breakpoints.Should().ContainKey(@"C:\app\Program.cs");
        session.Breakpoints[@"C:\app\Program.cs"].Should().Contain(b => b.Line == 42);
    }

    [TestMethod]
    public async Task Sends_SetBreakpoints_With_Correct_Payload()
    {
        var (tool, session, _) = CreateTool();

        var args = JsonNode.Parse("""{"sessionId":"sess1","file":"C:\\app\\Program.cs","line":42}""");
        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        session.SentRequests.Should().Contain(r => r.Command == "setBreakpoints");
    }

    [TestMethod]
    public async Task Returns_Verified_Breakpoints()
    {
        var (tool, _, _) = CreateTool();

        var args = JsonNode.Parse("""{"sessionId":"sess1","file":"C:\\app\\Program.cs","line":42}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        var text = GetText(result);
        text.Should().Contain("breakpoints");
    }

    [TestMethod]
    public async Task Does_Not_Add_Duplicate()
    {
        var (tool, session, _) = CreateTool();

        var args1 = JsonNode.Parse("""{"sessionId":"sess1","file":"C:\\app\\Program.cs","line":42}""");
        await tool.ExecuteAsync(JsonValue.Create(1), args1, CancellationToken.None);
        var args2 = JsonNode.Parse("""{"sessionId":"sess1","file":"C:\\app\\Program.cs","line":42}""");
        await tool.ExecuteAsync(JsonValue.Create(2), args2, CancellationToken.None);

        session.Breakpoints[@"C:\app\Program.cs"].Should().HaveCount(1);
    }

    [TestMethod]
    public async Task Missing_SessionId_Returns_Error()
    {
        var (tool, _, _) = CreateTool();

        var args = JsonNode.Parse("""{"file":"C:\\app\\Program.cs","line":42}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        HasErrorCode(result, -32602).Should().BeTrue();
    }

    [TestMethod]
    public async Task Missing_File_Returns_Error()
    {
        var (tool, _, _) = CreateTool();

        var args = JsonNode.Parse("""{"sessionId":"sess1","line":42}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        HasErrorCode(result, -32602).Should().BeTrue();
    }

    [TestMethod]
    public async Task Missing_Line_Returns_Error()
    {
        var (tool, _, _) = CreateTool();

        var args = JsonNode.Parse("""{"sessionId":"sess1","file":"C:\\app\\Program.cs"}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        HasErrorCode(result, -32602).Should().BeTrue();
    }

    [TestMethod]
    public async Task Session_Not_Found_Returns_Error()
    {
        var registry = FakeSessionRegistry.Empty();
        var logger = Substitute.For<ILogger<SetBreakpointTool>>();
        var tool = new SetBreakpointTool(registry, logger);

        var args = JsonNode.Parse("""{"sessionId":"unknown","file":"C:\\app\\Program.cs","line":42}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
    }

    [TestMethod]
    public async Task Stores_Condition_In_Breakpoint()
    {
        var (tool, session, _) = CreateTool();

        var args = JsonNode.Parse("""{"sessionId":"sess1","file":"C:\\app\\Program.cs","line":42,"condition":"i > 10"}""");
        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var bp = session.Breakpoints[@"C:\app\Program.cs"].Single();
        bp.Condition.Should().Be("i > 10");
    }

    [TestMethod]
    public async Task Stores_HitCount_In_Breakpoint()
    {
        var (tool, session, _) = CreateTool();

        var args = JsonNode.Parse("""{"sessionId":"sess1","file":"C:\\app\\Program.cs","line":42,"hitCount":"5"}""");
        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var bp = session.Breakpoints[@"C:\app\Program.cs"].Single();
        bp.HitCondition.Should().Be("5");
    }

    [TestMethod]
    public async Task Sends_Condition_In_Dap_Payload()
    {
        var (tool, session, _) = CreateTool();

        var args = JsonNode.Parse("""{"sessionId":"sess1","file":"C:\\app\\Program.cs","line":42,"condition":"x == 0","hitCount":"3"}""");
        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var req = session.SentRequests.First(r => r.Command == "setBreakpoints");
        var bps = req.Args!["breakpoints"] as JsonArray;
        bps.Should().NotBeNull();
        var bp = bps![0]!;
        bp["condition"]!.GetValue<string>().Should().Be("x == 0");
        bp["hitCondition"]!.GetValue<string>().Should().Be("3");
    }

    [TestMethod]
    public async Task Replacing_Breakpoint_At_Same_Line_Updates_Condition()
    {
        var (tool, session, _) = CreateTool();

        var args1 = JsonNode.Parse("""{"sessionId":"sess1","file":"C:\\app\\Program.cs","line":42,"condition":"old"}""");
        await tool.ExecuteAsync(JsonValue.Create(1), args1, CancellationToken.None);
        var args2 = JsonNode.Parse("""{"sessionId":"sess1","file":"C:\\app\\Program.cs","line":42,"condition":"new"}""");
        await tool.ExecuteAsync(JsonValue.Create(2), args2, CancellationToken.None);

        session.Breakpoints[@"C:\app\Program.cs"].Should().HaveCount(1);
        session.Breakpoints[@"C:\app\Program.cs"].Single().Condition.Should().Be("new");
    }
}
