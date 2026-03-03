using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using DebugMcpServer.Tests.Fakes;
using DebugMcpServer.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DebugMcpServer.Tests.Tests;

[TestClass]
public class SendDapRequestToolTests
{
    private static SendDapRequestTool CreateTool(FakeSession session, string sessionId = "sess1")
    {
        var registry = FakeSessionRegistry.WithSession(sessionId, session);
        return new SendDapRequestTool(registry, NullLogger<SendDapRequestTool>.Instance);
    }

    private static string GetText(JsonNode result) =>
        result["result"]!["content"]![0]!["text"]!.GetValue<string>();

    private static bool IsError(JsonNode result) =>
        result["result"]!["isError"]!.GetValue<bool>();

    [TestMethod]
    public async Task Sends_Command_And_Returns_Raw_Response()
    {
        var session = new FakeSession();
        session.SetupRequest("evaluate", JsonNode.Parse("""{"result":"42","type":"int"}""")!);
        var tool = CreateTool(session);
        var args = JsonNode.Parse("""{"sessionId":"sess1","command":"evaluate","arguments":{"expression":"x+1","frameId":1}}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        var json = JsonNode.Parse(GetText(result))!;
        json["command"]!.GetValue<string>().Should().Be("evaluate");
        json["response"]!["result"]!.GetValue<string>().Should().Be("42");
        json["response"]!["type"]!.GetValue<string>().Should().Be("int");
    }

    [TestMethod]
    public async Task Sends_Command_Without_Arguments()
    {
        var session = new FakeSession();
        session.SetupRequest("loadedSources", JsonNode.Parse("""{"sources":[]}""")!);
        var tool = CreateTool(session);
        var args = JsonNode.Parse("""{"sessionId":"sess1","command":"loadedSources"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        session.SentRequests.Should().ContainSingle(r => r.Command == "loadedSources");
        session.SentRequests[0].Args.Should().BeNull();
    }

    [TestMethod]
    public async Task Passes_Arguments_Through_To_Session()
    {
        var session = new FakeSession();
        session.SetupRequest("modules", JsonNode.Parse("""{"modules":[]}""")!);
        var tool = CreateTool(session);
        var args = JsonNode.Parse("""{"sessionId":"sess1","command":"modules","arguments":{"startModule":0,"moduleCount":10}}""");

        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        session.SentRequests.Should().ContainSingle(r => r.Command == "modules");
        // Arguments are passed as JsonNode — verify the request was sent
        session.SentRequests[0].Args.Should().NotBeNull();
    }

    [TestMethod]
    public async Task DAP_Error_Returns_IsError_True()
    {
        var session = new FakeSession();
        session.SetupRequestError("evaluate", "not stopped");
        var tool = CreateTool(session);
        var args = JsonNode.Parse("""{"sessionId":"sess1","command":"evaluate"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("evaluate");
        GetText(result).Should().Contain("not stopped");
    }

    [TestMethod]
    public async Task Missing_SessionId_Returns_Error()
    {
        var tool = CreateTool(new FakeSession());
        var args = JsonNode.Parse("""{"command":"evaluate"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        result["error"]!["code"]!.GetValue<int>().Should().Be(-32602);
    }

    [TestMethod]
    public async Task Missing_Command_Returns_Error()
    {
        var tool = CreateTool(new FakeSession());
        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        result["error"]!["code"]!.GetValue<int>().Should().Be(-32602);
    }

    [TestMethod]
    public async Task Session_Not_Found_Returns_IsError()
    {
        var tool = CreateTool(new FakeSession());
        var args = JsonNode.Parse("""{"sessionId":"unknown","command":"evaluate"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("unknown");
    }

    [TestMethod]
    public async Task Response_Command_Field_Matches_Requested_Command()
    {
        var session = new FakeSession();
        session.SetupRequest("exceptionInfo", JsonNode.Parse("""{"exceptionId":"NullRef"}""")!);
        var tool = CreateTool(session);
        var args = JsonNode.Parse("""{"sessionId":"sess1","command":"exceptionInfo","arguments":{"threadId":1}}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var json = JsonNode.Parse(GetText(result))!;
        json["command"]!.GetValue<string>().Should().Be("exceptionInfo");
    }
}
