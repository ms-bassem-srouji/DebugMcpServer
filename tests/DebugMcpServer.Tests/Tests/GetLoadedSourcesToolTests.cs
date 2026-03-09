using System.Text.Json.Nodes;
using DebugMcpServer.Tools;
using DebugMcpServer.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DebugMcpServer.Tests.Tests;

[TestClass]
public class GetLoadedSourcesToolTests
{
    private static string GetText(JsonNode result) =>
        result["result"]!["content"]![0]!["text"]!.GetValue<string>();

    private static bool IsError(JsonNode result) =>
        result["result"]!["isError"]!.GetValue<bool>();

    private static (GetLoadedSourcesTool tool, FakeSession session) CreateTool()
    {
        var session = new FakeSession();
        session.SetupRequest("loadedSources", JsonNode.Parse("""
            {
                "sources": [
                    {"name": "main.c", "path": "/app/main.c"},
                    {"name": "utils.c", "path": "/app/utils.c", "origin": "debug symbols"}
                ]
            }
            """)!);
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<GetLoadedSourcesTool>>();
        return (new GetLoadedSourcesTool(registry, logger), session);
    }

    [TestMethod]
    public void Name_Is_get_loaded_sources()
    {
        var (tool, _) = CreateTool();
        tool.Name.Should().Be("get_loaded_sources");
    }

    [TestMethod]
    public void Description_Is_Not_Empty()
    {
        var (tool, _) = CreateTool();
        tool.Description.Should().NotBeNullOrWhiteSpace();
    }

    [TestMethod]
    public void InputSchema_Has_SessionId_Required()
    {
        var (tool, _) = CreateTool();
        var schema = tool.GetInputSchema();
        var required = schema["required"] as JsonArray;
        required.Should().NotBeNull();
        required!.Select(r => r!.GetValue<string>()).Should().Contain("sessionId");
    }

    [TestMethod]
    public async Task Returns_Sources_With_Details()
    {
        var (tool, _) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        var json = JsonNode.Parse(GetText(result))!;
        json["count"]!.GetValue<int>().Should().Be(2);

        var sources = (json["sources"] as JsonArray)!;
        sources[0]!["name"]!.GetValue<string>().Should().Be("main.c");
        sources[0]!["path"]!.GetValue<string>().Should().Be("/app/main.c");
        sources[1]!["origin"]!.GetValue<string>().Should().Be("debug symbols");
    }

    [TestMethod]
    public async Task Sends_LoadedSources_Dap_Request()
    {
        var (tool, session) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");

        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        session.SentRequests.Should().Contain(r => r.Command == "loadedSources");
    }

    [TestMethod]
    public async Task Session_Not_Found_Returns_Error()
    {
        var registry = FakeSessionRegistry.Empty();
        var logger = Substitute.For<ILogger<GetLoadedSourcesTool>>();
        var tool = new GetLoadedSourcesTool(registry, logger);
        var args = JsonNode.Parse("""{"sessionId":"unknown"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
    }

    [TestMethod]
    public async Task Dap_Error_Returns_Error_Result()
    {
        var session = new FakeSession();
        session.SetupRequestError("loadedSources", "Not supported");
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<GetLoadedSourcesTool>>();
        var tool = new GetLoadedSourcesTool(registry, logger);
        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("Not supported");
    }

    [TestMethod]
    public async Task Missing_SessionId_Returns_Error()
    {
        var (tool, _) = CreateTool();
        var args = JsonNode.Parse("""{}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        result["error"].Should().NotBeNull();
    }

    [TestMethod]
    public async Task Empty_Sources_Returns_Message()
    {
        var session = new FakeSession();
        session.SetupRequest("loadedSources", JsonNode.Parse("""{"sources": []}""")!);
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<GetLoadedSourcesTool>>();
        var tool = new GetLoadedSourcesTool(registry, logger);
        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        var json = JsonNode.Parse(GetText(result))!;
        json["count"]!.GetValue<int>().Should().Be(0);
        json["message"].Should().NotBeNull();
    }

    [TestMethod]
    public async Task Null_Source_In_Array_Is_Skipped()
    {
        var session = new FakeSession();
        session.SetupRequest("loadedSources", JsonNode.Parse("""
            {
                "sources": [
                    {"name": "main.c", "path": "/app/main.c"},
                    null,
                    {"name": "utils.c", "path": "/app/utils.c"}
                ]
            }
            """)!);
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<GetLoadedSourcesTool>>();
        var tool = new GetLoadedSourcesTool(registry, logger);
        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var json = JsonNode.Parse(GetText(result))!;
        json["count"]!.GetValue<int>().Should().Be(2);
    }

    [TestMethod]
    public async Task Source_Without_Origin_Still_Returns()
    {
        var session = new FakeSession();
        session.SetupRequest("loadedSources", JsonNode.Parse("""
            { "sources": [{"name": "main.c", "path": "/app/main.c"}] }
            """)!);
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<GetLoadedSourcesTool>>();
        var tool = new GetLoadedSourcesTool(registry, logger);
        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var json = JsonNode.Parse(GetText(result))!;
        json["count"]!.GetValue<int>().Should().Be(1);
        json["sources"]![0]!["origin"].Should().BeNull();
    }

    [TestMethod]
    public async Task Null_Sources_Array_Returns_Empty()
    {
        var session = new FakeSession();
        session.SetupRequest("loadedSources", JsonNode.Parse("""{}""")!);
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<GetLoadedSourcesTool>>();
        var tool = new GetLoadedSourcesTool(registry, logger);
        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        var json = JsonNode.Parse(GetText(result))!;
        json["count"]!.GetValue<int>().Should().Be(0);
    }
}
