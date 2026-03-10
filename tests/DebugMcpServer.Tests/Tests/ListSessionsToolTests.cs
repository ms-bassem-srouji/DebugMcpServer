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

    private static ListSessionsTool CreateTool(DapSessionRegistry registry)
        => new ListSessionsTool(registry, FakeDotnetDumpRegistry.Empty(), FakeNativeDumpRegistry.Empty());

    [TestMethod]
    public async Task Returns_Active_Sessions()
    {
        var session1 = new FakeSession { State = SessionState.Paused, ActiveThreadId = 1 };
        var session2 = new FakeSession { State = SessionState.Running, ActiveThreadId = 5 };
        var registry = FakeSessionRegistry.WithSession("s1", session1);
        registry.Register("s2", session2);

        var tool = CreateTool(registry);

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
        var tool = CreateTool(registry);

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        var json = JsonNode.Parse(GetText(result))!;
        json["count"]!.GetValue<int>().Should().Be(0);
        (json["sessions"] as JsonArray).Should().BeEmpty();
    }

    [TestMethod]
    public async Task IsError_Is_False()
    {
        var registry = FakeSessionRegistry.Empty();
        var tool = CreateTool(registry);

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        result["result"]!["isError"]!.GetValue<bool>().Should().BeFalse();
    }

    [TestMethod]
    public async Task DAP_Sessions_Show_Type_Dap()
    {
        var session = new FakeSession { State = SessionState.Paused, ActiveThreadId = 1 };
        var registry = FakeSessionRegistry.WithSession("s1", session);
        var tool = CreateTool(registry);

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        var json = JsonNode.Parse(GetText(result))!;
        var sessions = (json["sessions"] as JsonArray)!;
        sessions[0]!["type"]!.GetValue<string>().Should().Be("dap");
    }

    [TestMethod]
    public async Task DAP_DumpSession_Shows_IsDumpSession_True()
    {
        var session = new FakeSession { State = SessionState.Paused, IsDumpSession = true };
        var registry = FakeSessionRegistry.WithSession("dump1", session);
        var tool = CreateTool(registry);

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        var json = JsonNode.Parse(GetText(result))!;
        var sessions = (json["sessions"] as JsonArray)!;
        sessions[0]!["isDumpSession"]!.GetValue<bool>().Should().BeTrue();
    }

    [TestMethod]
    public async Task Both_Registries_Empty_Returns_Zero_Count()
    {
        var dapRegistry = FakeSessionRegistry.Empty();
        var dumpRegistry = FakeDotnetDumpRegistry.Empty();
        var tool = new ListSessionsTool(dapRegistry, dumpRegistry, FakeNativeDumpRegistry.Empty());

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        var json = JsonNode.Parse(GetText(result))!;
        json["count"]!.GetValue<int>().Should().Be(0);
        (json["sessions"] as JsonArray).Should().BeEmpty();
    }

    [TestMethod]
    public void Name_Is_list_sessions()
    {
        var tool = CreateTool(FakeSessionRegistry.Empty());
        tool.Name.Should().Be("list_sessions");
    }

    [TestMethod]
    public void Description_Mentions_All_Session_Types()
    {
        var tool = CreateTool(FakeSessionRegistry.Empty());
        tool.Description.Should().Contain("DAP");
        tool.Description.Should().Contain("dotnet-dump");
        tool.Description.Should().Contain("native dump");
    }

    [TestMethod]
    public void InputSchema_Has_No_Required_Fields()
    {
        var tool = CreateTool(FakeSessionRegistry.Empty());
        var required = tool.GetInputSchema()["required"] as JsonArray;
        required.Should().NotBeNull();
        required!.Should().BeEmpty();
    }

    [TestMethod]
    public async Task Running_Session_Shows_Running_State()
    {
        var session = new FakeSession { State = SessionState.Running };
        var registry = FakeSessionRegistry.WithSession("r1", session);
        var tool = CreateTool(registry);

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        var json = JsonNode.Parse(GetText(result))!;
        var sessions = (json["sessions"] as JsonArray)!;
        sessions[0]!["state"]!.GetValue<string>().Should().Be("Running");
    }

    [TestMethod]
    public async Task Non_DumpSession_Shows_IsDumpSession_False()
    {
        var session = new FakeSession { State = SessionState.Paused, IsDumpSession = false };
        var registry = FakeSessionRegistry.WithSession("live1", session);
        var tool = CreateTool(registry);

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        var json = JsonNode.Parse(GetText(result))!;
        var sessions = (json["sessions"] as JsonArray)!;
        sessions[0]!["isDumpSession"]!.GetValue<bool>().Should().BeFalse();
    }

    [TestMethod]
    public async Task Null_ActiveThread_Serializes_Without_Error()
    {
        var session = new FakeSession { State = SessionState.Paused, ActiveThreadId = null };
        var registry = FakeSessionRegistry.WithSession("s1", session);
        var tool = CreateTool(registry);

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);

        // The tool should still return successfully when ActiveThreadId is null
        result["result"]!["isError"]!.GetValue<bool>().Should().BeFalse();
        var json = JsonNode.Parse(GetText(result))!;
        var sessions = (json["sessions"] as JsonArray)!;
        sessions.Should().HaveCount(1);
        sessions[0]!["sessionId"]!.GetValue<string>().Should().Be("s1");
    }

    [TestMethod]
    public async Task Arguments_Are_Ignored()
    {
        var registry = FakeSessionRegistry.Empty();
        var tool = CreateTool(registry);

        // Pass arbitrary arguments — tool should ignore them
        var args = JsonNode.Parse("""{"randomKey": "randomValue"}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        result["result"]!["isError"]!.GetValue<bool>().Should().BeFalse();
    }
}
