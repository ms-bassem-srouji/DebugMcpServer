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
public class ReadMemoryToolTests
{
    private static string GetText(JsonNode result) =>
        result["result"]!["content"]![0]!["text"]!.GetValue<string>();

    private static bool IsError(JsonNode result) =>
        result["result"]!["isError"]!.GetValue<bool>();

    private static (ReadMemoryTool tool, FakeSession session) CreateTool()
    {
        var session = new FakeSession();
        // Return 4 bytes: 0x48 0x65 0x6C 0x6C = "Hell"
        session.SetupRequest("readMemory", JsonNode.Parse("""
            {"address":"0x1000","data":"SGVsbA==","unreadableBytes":0}
            """)!);
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<ReadMemoryTool>>();
        return (new ReadMemoryTool(registry, logger), session);
    }

    [TestMethod]
    public async Task Returns_Memory_Data_And_HexDump()
    {
        var (tool, _) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1","memoryReference":"0x1000","count":4}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        var json = JsonNode.Parse(GetText(result))!;
        json["address"]!.GetValue<string>().Should().Be("0x1000");
        json["bytesRead"]!.GetValue<int>().Should().Be(4);
        json["data"]!.GetValue<string>().Should().Be("SGVsbA==");
        json["hexDump"]!.GetValue<string>().Should().Contain("48 65 6C 6C");
    }

    [TestMethod]
    public async Task Sends_ReadMemory_Dap_Request()
    {
        var (tool, session) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1","memoryReference":"0x1000","offset":8,"count":16}""");

        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        session.SentRequests.Should().Contain(r => r.Command == "readMemory");
    }

    [TestMethod]
    public async Task Missing_MemoryReference_Returns_Error()
    {
        var (tool, _) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        result["error"]!["code"]!.GetValue<int>().Should().Be(-32602);
    }

    [TestMethod]
    public async Task Session_Not_Found_Returns_Error()
    {
        var registry = FakeSessionRegistry.Empty();
        var logger = Substitute.For<ILogger<ReadMemoryTool>>();
        var tool = new ReadMemoryTool(registry, logger);
        var args = JsonNode.Parse("""{"sessionId":"unknown","memoryReference":"0x1000"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
    }

    [TestMethod]
    public async Task Dap_Error_Returns_Error_Result()
    {
        var session = new FakeSession();
        session.SetupRequestError("readMemory", "Memory not readable");
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<ReadMemoryTool>>();
        var tool = new ReadMemoryTool(registry, logger);
        var args = JsonNode.Parse("""{"sessionId":"sess1","memoryReference":"0xBAD"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
    }
}
