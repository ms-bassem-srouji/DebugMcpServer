using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using DebugMcpServer.Tools;
using DebugMcpServer.Tests.Fakes;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DebugMcpServer.Tests.Tests;

[TestClass]
public class ListSessionsToolTests
{
    private static string GetText(JsonNode result) =>
        result["result"]!["content"]![0]!["text"]!.GetValue<string>();

    [TestMethod]
    public async Task Returns_Active_Sessions()
    {
        var session1 = new FakeSession { State = SessionState.Paused, ActiveThreadId = 1 };
        var session2 = new FakeSession { State = SessionState.Running, ActiveThreadId = 5 };
        var registry = FakeSessionRegistry.WithSession("s1", session1);
        registry.Register("s2", session2);

        var tool = new ListSessionsTool(registry);

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        var json = JsonNode.Parse(GetText(result))!;
        json["count"]!.GetValue<int>().Should().Be(2);
        var sessions = (json["sessions"] as JsonArray)!;
        sessions.Should().HaveCount(2);

        // Find sessions by ID (order not guaranteed from ConcurrentDictionary)
        var s1 = sessions.First(s => s!["sessionId"]!.GetValue<string>() == "s1")!;
        s1["state"]!.GetValue<string>().Should().Be("Paused");
        s1["activeThreadId"]!.GetValue<int>().Should().Be(1);

        var s2 = sessions.First(s => s!["sessionId"]!.GetValue<string>() == "s2")!;
        s2["state"]!.GetValue<string>().Should().Be("Running");
        s2["activeThreadId"]!.GetValue<int>().Should().Be(5);
    }

    [TestMethod]
    public async Task Empty_Registry_Returns_Empty_List()
    {
        var registry = FakeSessionRegistry.Empty();
        var tool = new ListSessionsTool(registry);

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        var json = JsonNode.Parse(GetText(result))!;
        json["count"]!.GetValue<int>().Should().Be(0);
        (json["sessions"] as JsonArray).Should().BeEmpty();
    }

    [TestMethod]
    public async Task IsError_Is_False()
    {
        var registry = FakeSessionRegistry.Empty();
        var tool = new ListSessionsTool(registry);

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        result["result"]!["isError"]!.GetValue<bool>().Should().BeFalse();
    }
}
