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
public class WriteMemoryToolTests
{
    private static string GetText(JsonNode result) =>
        result["result"]!["content"]![0]!["text"]!.GetValue<string>();

    private static bool IsError(JsonNode result) =>
        result["result"]!["isError"]!.GetValue<bool>();

    private static (WriteMemoryTool tool, FakeSession session) CreateTool()
    {
        var session = new FakeSession();
        session.SetupRequest("writeMemory", JsonNode.Parse("""{"bytesWritten":4}""")!);
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<WriteMemoryTool>>();
        return (new WriteMemoryTool(registry, logger), session);
    }

    [TestMethod]
    public async Task Writes_Hex_Data_Successfully()
    {
        var (tool, session) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1","memoryReference":"0x1000","data":"48656C6C"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        var json = JsonNode.Parse(GetText(result))!;
        json["bytesWritten"]!.GetValue<int>().Should().Be(4);
        session.SentRequests.Should().Contain(r => r.Command == "writeMemory");
    }

    [TestMethod]
    public async Task Writes_Base64_Data_Successfully()
    {
        var (tool, _) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1","memoryReference":"0x1000","data":"SGVsbA==","encoding":"base64"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
    }

    [TestMethod]
    public async Task Invalid_Hex_Returns_Error()
    {
        var (tool, _) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1","memoryReference":"0x1000","data":"ZZZZ"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("Invalid data format");
    }

    [TestMethod]
    public async Task Missing_Data_Returns_Error()
    {
        var (tool, _) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1","memoryReference":"0x1000"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        result["error"]!["code"]!.GetValue<int>().Should().Be(-32602);
    }

    [TestMethod]
    public async Task Session_Not_Found_Returns_Error()
    {
        var registry = FakeSessionRegistry.Empty();
        var logger = Substitute.For<ILogger<WriteMemoryTool>>();
        var tool = new WriteMemoryTool(registry, logger);
        var args = JsonNode.Parse("""{"sessionId":"unknown","memoryReference":"0x1000","data":"FF"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
    }

    [TestMethod]
    public async Task Dap_Error_Returns_Error_Result()
    {
        var session = new FakeSession();
        session.SetupRequestError("writeMemory", "Access denied");
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<WriteMemoryTool>>();
        var tool = new WriteMemoryTool(registry, logger);
        var args = JsonNode.Parse("""{"sessionId":"sess1","memoryReference":"0xBAD","data":"FF"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
    }
}
