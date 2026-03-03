using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using DebugMcpServer.Tools;
using DebugMcpServer.Tests.Fakes;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DebugMcpServer.Tests.Tests;

[TestClass]
public class ListBreakpointsToolTests
{
    private static string GetText(JsonNode result) =>
        result["result"]!["content"]![0]!["text"]!.GetValue<string>();

    private static bool IsError(JsonNode result) =>
        result["result"]!["isError"]!.GetValue<bool>();

    [TestMethod]
    public async Task Returns_All_Breakpoints()
    {
        var session = new FakeSession();
        session.Breakpoints["C:\\app\\Program.cs"] = new List<SourceBreakpoint>
        {
            new("C:\\app\\Program.cs", 10),
            new("C:\\app\\Program.cs", 25)
        };
        session.Breakpoints["C:\\app\\Foo.cs"] = new List<SourceBreakpoint>
        {
            new("C:\\app\\Foo.cs", 5)
        };
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var tool = new ListBreakpointsTool(registry);
        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        var json = JsonNode.Parse(GetText(result))!;
        json["count"]!.GetValue<int>().Should().Be(3);
        var bps = (json["breakpoints"] as JsonArray)!;
        bps.Should().HaveCount(3);
    }

    [TestMethod]
    public async Task Includes_Condition_And_HitCondition()
    {
        var session = new FakeSession();
        session.Breakpoints["C:\\app\\Program.cs"] = new List<SourceBreakpoint>
        {
            new("C:\\app\\Program.cs", 10, Condition: "i > 5", HitCondition: "3")
        };
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var tool = new ListBreakpointsTool(registry);
        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var json = JsonNode.Parse(GetText(result))!;
        var bp = json["breakpoints"]![0]!;
        bp["condition"]!.GetValue<string>().Should().Be("i > 5");
        bp["hitCondition"]!.GetValue<string>().Should().Be("3");
    }

    [TestMethod]
    public async Task Omits_Condition_When_Null()
    {
        var session = new FakeSession();
        session.Breakpoints["C:\\app\\Program.cs"] = new List<SourceBreakpoint>
        {
            new("C:\\app\\Program.cs", 10)
        };
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var tool = new ListBreakpointsTool(registry);
        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var json = JsonNode.Parse(GetText(result))!;
        var bp = json["breakpoints"]![0]!;
        bp["condition"].Should().BeNull();
        bp["hitCondition"].Should().BeNull();
    }

    [TestMethod]
    public async Task Empty_Breakpoints_Returns_Empty_List()
    {
        var session = new FakeSession();
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var tool = new ListBreakpointsTool(registry);
        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var json = JsonNode.Parse(GetText(result))!;
        json["count"]!.GetValue<int>().Should().Be(0);
        (json["breakpoints"] as JsonArray).Should().BeEmpty();
    }

    [TestMethod]
    public async Task Session_Not_Found_Returns_Error()
    {
        var registry = FakeSessionRegistry.Empty();
        var tool = new ListBreakpointsTool(registry);
        var args = JsonNode.Parse("""{"sessionId":"unknown"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
    }

    [TestMethod]
    public async Task Missing_SessionId_Returns_Error()
    {
        var registry = FakeSessionRegistry.Empty();
        var tool = new ListBreakpointsTool(registry);

        var result = await tool.ExecuteAsync(JsonValue.Create(1), JsonNode.Parse("{}")!, CancellationToken.None);

        result["error"]!["code"]!.GetValue<int>().Should().Be(-32602);
    }
}
