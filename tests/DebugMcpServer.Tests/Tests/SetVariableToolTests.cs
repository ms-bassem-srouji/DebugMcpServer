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
public class SetVariableToolTests
{
    private static string GetText(JsonNode result) =>
        result["result"]!["content"]![0]!["text"]!.GetValue<string>();

    private static bool IsError(JsonNode result) =>
        result["result"]!["isError"]!.GetValue<bool>();

    private static bool HasErrorCode(JsonNode result, int code) =>
        result["error"]?["code"]?.GetValue<int>() == code;

    private static (SetVariableTool tool, FakeSession session) CreateTool()
    {
        var session = new FakeSession { ActiveThreadId = 1 };
        session.SetupRequest("setVariable", JsonNode.Parse("""{"value":"99","type":"int"}""")!);
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<SetVariableTool>>();
        return (new SetVariableTool(registry, logger), session);
    }

    [TestMethod]
    public async Task Sends_SetVariable_With_Correct_Parameters()
    {
        var (tool, session) = CreateTool();

        var args = JsonNode.Parse("""{"sessionId":"sess1","variablesReference":1,"name":"x","value":"99"}""");
        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        session.SentRequests.Should().HaveCount(1);
        session.SentRequests[0].Command.Should().Be("setVariable");
        var sentArgs = session.SentRequests[0].Args;
        sentArgs!["variablesReference"]!.GetValue<int>().Should().Be(1);
        sentArgs["name"]!.GetValue<string>().Should().Be("x");
        sentArgs["value"]!.GetValue<string>().Should().Be("99");
    }

    [TestMethod]
    public async Task Returns_New_Value_From_Response()
    {
        var (tool, _) = CreateTool();

        var args = JsonNode.Parse("""{"sessionId":"sess1","variablesReference":1,"name":"x","value":"99"}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        var text = GetText(result);
        text.Should().Contain("99");
        text.Should().Contain("int");
        text.Should().Contain("ok");
    }

    [TestMethod]
    public async Task Missing_Name_Returns_Error()
    {
        var (tool, _) = CreateTool();

        var args = JsonNode.Parse("""{"sessionId":"sess1","variablesReference":1,"value":"99"}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        HasErrorCode(result, -32602).Should().BeTrue();
    }

    [TestMethod]
    public async Task Missing_Value_Returns_Error()
    {
        var (tool, _) = CreateTool();

        var args = JsonNode.Parse("""{"sessionId":"sess1","variablesReference":1,"name":"x"}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        HasErrorCode(result, -32602).Should().BeTrue();
    }

    [TestMethod]
    public async Task Missing_VariablesReference_Returns_Error()
    {
        var (tool, _) = CreateTool();

        var args = JsonNode.Parse("""{"sessionId":"sess1","name":"x","value":"99"}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        HasErrorCode(result, -32602).Should().BeTrue();
    }

    [TestMethod]
    public async Task DAP_Error_Returns_IsError()
    {
        var session = new FakeSession { ActiveThreadId = 1 };
        session.SetupRequestError("setVariable", "read-only variable");
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<SetVariableTool>>();
        var tool = new SetVariableTool(registry, logger);

        var args = JsonNode.Parse("""{"sessionId":"sess1","variablesReference":1,"name":"x","value":"99"}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
        var text = GetText(result);
        text.Should().Contain("DAP error");
    }
}
