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
public class SetDataBreakpointToolTests
{
    private static string GetText(JsonNode result) =>
        result["result"]!["content"]![0]!["text"]!.GetValue<string>();

    private static bool IsError(JsonNode result) =>
        result["result"]!["isError"]!.GetValue<bool>();

    private static bool HasErrorCode(JsonNode result, int code) =>
        result["error"]?["code"]?.GetValue<int>() == code;

    private static (SetDataBreakpointTool tool, FakeSession session) CreateTool()
    {
        var session = new FakeSession { ActiveThreadId = 1 };
        session.SetupRequest("dataBreakpointInfo", JsonNode.Parse("""
            {"dataId":"myVar_addr_0x1234","description":"myVar","accessTypes":["read","write","readWrite"]}
            """)!);
        session.SetupRequest("setDataBreakpoints", JsonNode.Parse("""
            {"breakpoints":[{"id":1,"dataId":"myVar_addr_0x1234","verified":true,"message":null}]}
            """)!);
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<SetDataBreakpointTool>>();
        return (new SetDataBreakpointTool(registry, logger), session);
    }

    [TestMethod]
    public async Task Sets_Data_Breakpoint_With_Raw_DataId()
    {
        var (tool, session) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1","dataId":"myVar_addr_0x1234"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        var text = GetText(result);
        text.Should().Contain("myVar_addr_0x1234");
        session.SentRequests.Should().Contain(r => r.Command == "setDataBreakpoints");
        session.SentRequests.Should().NotContain(r => r.Command == "dataBreakpointInfo");
    }

    [TestMethod]
    public async Task Resolves_DataId_From_VariablesReference_And_Name()
    {
        var (tool, session) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1","variablesReference":10,"name":"myVar"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        session.SentRequests.Should().Contain(r => r.Command == "dataBreakpointInfo");
        session.SentRequests.Should().Contain(r => r.Command == "setDataBreakpoints");
        var text = GetText(result);
        text.Should().Contain("myVar_addr_0x1234");
    }

    [TestMethod]
    public async Task Sends_AccessType_In_Payload()
    {
        var (tool, session) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1","dataId":"myVar_addr_0x1234","accessType":"readWrite"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        var req = session.SentRequests.First(r => r.Command == "setDataBreakpoints");
        var bps = req.Args!["breakpoints"] as JsonArray;
        bps.Should().NotBeNull();
        bps![0]!["accessType"]!.GetValue<string>().Should().Be("readWrite");
    }

    [TestMethod]
    public async Task Defaults_AccessType_To_Write()
    {
        var (tool, session) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1","dataId":"myVar_addr_0x1234"}""");

        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var req = session.SentRequests.First(r => r.Command == "setDataBreakpoints");
        var bps = req.Args!["breakpoints"] as JsonArray;
        bps![0]!["accessType"]!.GetValue<string>().Should().Be("write");
    }

    [TestMethod]
    public async Task Sends_Condition_And_HitCondition_In_Payload()
    {
        var (tool, session) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1","dataId":"myVar_addr_0x1234","condition":"x > 5","hitCondition":"3"}""");

        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var req = session.SentRequests.First(r => r.Command == "setDataBreakpoints");
        var bps = req.Args!["breakpoints"] as JsonArray;
        bps![0]!["condition"]!.GetValue<string>().Should().Be("x > 5");
        bps![0]!["hitCondition"]!.GetValue<string>().Should().Be("3");
    }

    [TestMethod]
    public async Task Returns_Verified_Breakpoint_Info()
    {
        var (tool, _) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1","dataId":"myVar_addr_0x1234"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        var json = JsonNode.Parse(GetText(result))!;
        json["dataId"]!.GetValue<string>().Should().Be("myVar_addr_0x1234");
        json["accessType"]!.GetValue<string>().Should().Be("write");
        var bps = json["breakpoints"] as JsonArray;
        bps.Should().HaveCount(1);
        bps![0]!["verified"]!.GetValue<bool>().Should().BeTrue();
    }

    [TestMethod]
    public async Task Missing_SessionId_Returns_Error()
    {
        var (tool, _) = CreateTool();
        var args = JsonNode.Parse("""{"dataId":"myVar_addr_0x1234"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        HasErrorCode(result, -32602).Should().BeTrue();
    }

    [TestMethod]
    public async Task Missing_DataId_And_VariablesReference_Returns_Error()
    {
        var (tool, _) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        HasErrorCode(result, -32602).Should().BeTrue();
    }

    [TestMethod]
    public async Task Missing_Name_When_VariablesReference_Provided_Returns_Error()
    {
        var (tool, _) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1","variablesReference":10}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        HasErrorCode(result, -32602).Should().BeTrue();
    }

    [TestMethod]
    public async Task Session_Not_Found_Returns_Error()
    {
        var registry = FakeSessionRegistry.Empty();
        var logger = Substitute.For<ILogger<SetDataBreakpointTool>>();
        var tool = new SetDataBreakpointTool(registry, logger);
        var args = JsonNode.Parse("""{"sessionId":"unknown","dataId":"myVar_addr_0x1234"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("not found");
    }

    [TestMethod]
    public async Task Running_Session_Returns_Error()
    {
        var (tool, session) = CreateTool();
        session.State = SessionState.Running;
        var args = JsonNode.Parse("""{"sessionId":"sess1","dataId":"myVar_addr_0x1234"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("running");
    }

    [TestMethod]
    public async Task Null_DataId_From_Info_Returns_Error()
    {
        var session = new FakeSession { ActiveThreadId = 1 };
        session.SetupRequest("dataBreakpointInfo", JsonNode.Parse("""
            {"dataId":null,"description":"myVar","accessTypes":[]}
            """)!);
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<SetDataBreakpointTool>>();
        var tool = new SetDataBreakpointTool(registry, logger);
        var args = JsonNode.Parse("""{"sessionId":"sess1","variablesReference":10,"name":"myVar"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("not supported");
    }

    [TestMethod]
    public async Task DataBreakpointInfo_Dap_Error_Returns_Error()
    {
        var session = new FakeSession { ActiveThreadId = 1 };
        session.SetupRequestError("dataBreakpointInfo", "not supported");
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<SetDataBreakpointTool>>();
        var tool = new SetDataBreakpointTool(registry, logger);
        var args = JsonNode.Parse("""{"sessionId":"sess1","variablesReference":10,"name":"myVar"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("Failed to query data breakpoint info");
    }

    [TestMethod]
    public async Task SetDataBreakpoints_Dap_Error_Returns_Error()
    {
        var session = new FakeSession { ActiveThreadId = 1 };
        session.SetupRequestError("setDataBreakpoints", "hardware watchpoints not available");
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<SetDataBreakpointTool>>();
        var tool = new SetDataBreakpointTool(registry, logger);
        var args = JsonNode.Parse("""{"sessionId":"sess1","dataId":"myVar_addr_0x1234"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("DAP error");
    }
}
