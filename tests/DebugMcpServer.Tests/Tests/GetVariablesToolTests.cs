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
public class GetVariablesToolTests
{
    private static string GetText(JsonNode result) =>
        result["result"]!["content"]![0]!["text"]!.GetValue<string>();

    private static bool IsError(JsonNode result) =>
        result["result"]!["isError"]!.GetValue<bool>();

    private static bool HasErrorCode(JsonNode result, int code) =>
        result["error"]?["code"]?.GetValue<int>() == code;

    [TestMethod]
    public async Task FrameId_Mode_Fetches_Scopes_Then_Variables()
    {
        var session = new FakeSession { ActiveThreadId = 1 };
        session.SetupRequest("scopes", JsonNode.Parse("""{"scopes":[{"name":"Locals","variablesReference":1,"expensive":false}]}""")!);
        session.SetupRequest("variables", JsonNode.Parse("""{"variables":[{"name":"x","value":"42","type":"int","variablesReference":0}]}""")!);
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<GetVariablesTool>>();
        var tool = new GetVariablesTool(registry, logger);

        var args = JsonNode.Parse("""{"sessionId":"sess1","frameId":1}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        var text = GetText(result);
        text.Should().Contain("scopes");
        text.Should().Contain("Locals");
        text.Should().Contain("x");
        text.Should().Contain("42");

        session.SentRequests.Should().Contain(r => r.Command == "scopes");
        session.SentRequests.Should().Contain(r => r.Command == "variables");
    }

    [TestMethod]
    public async Task VariablesReference_Mode_Directly_Fetches_Variables()
    {
        var session = new FakeSession { ActiveThreadId = 1 };
        session.SetupRequest("variables", JsonNode.Parse("""{"variables":[{"name":"y","value":"hello","type":"string","variablesReference":0}]}""")!);
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<GetVariablesTool>>();
        var tool = new GetVariablesTool(registry, logger);

        var args = JsonNode.Parse("""{"sessionId":"sess1","variablesReference":10}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        session.SentRequests.Should().Contain(r => r.Command == "variables");
        session.SentRequests.Should().NotContain(r => r.Command == "scopes");
        var text = GetText(result);
        text.Should().Contain("y");
        text.Should().Contain("hello");
    }

    [TestMethod]
    public async Task Expensive_Scope_Not_Expanded()
    {
        var session = new FakeSession { ActiveThreadId = 1 };
        session.SetupRequest("scopes", JsonNode.Parse("""{"scopes":[{"name":"Globals","variablesReference":99,"expensive":true}]}""")!);
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<GetVariablesTool>>();
        var tool = new GetVariablesTool(registry, logger);

        var args = JsonNode.Parse("""{"sessionId":"sess1","frameId":1}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        var text = GetText(result);
        text.Should().Contain("expensive");
        text.Should().Contain("note");
        session.SentRequests.Should().NotContain(r => r.Command == "variables");
    }

    [TestMethod]
    public async Task Variable_With_NonZero_Ref_Marked_Expandable()
    {
        var session = new FakeSession { ActiveThreadId = 1 };
        session.SetupRequest("scopes", JsonNode.Parse("""{"scopes":[{"name":"Locals","variablesReference":1,"expensive":false}]}""")!);
        session.SetupRequest("variables", JsonNode.Parse("""{"variables":[{"name":"obj","value":"{...}","type":"MyClass","variablesReference":5}]}""")!);
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<GetVariablesTool>>();
        var tool = new GetVariablesTool(registry, logger);

        var args = JsonNode.Parse("""{"sessionId":"sess1","frameId":1}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        var text = GetText(result);
        text.Should().Contain("expandable");
        text.Should().Contain("true");
    }

    [TestMethod]
    public async Task Missing_Both_FrameId_And_Reference_Returns_Error()
    {
        var session = new FakeSession { ActiveThreadId = 1 };
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<GetVariablesTool>>();
        var tool = new GetVariablesTool(registry, logger);

        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        HasErrorCode(result, -32602).Should().BeTrue();
    }

    [TestMethod]
    public async Task DAP_Error_Returns_IsError()
    {
        var session = new FakeSession { ActiveThreadId = 1 };
        session.SetupRequestError("scopes", "frame not found");
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<GetVariablesTool>>();
        var tool = new GetVariablesTool(registry, logger);

        var args = JsonNode.Parse("""{"sessionId":"sess1","frameId":1}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
        var text = GetText(result);
        text.Should().Contain("frame not found");
    }
}
