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
public class DisassembleToolTests
{
    private static string GetText(JsonNode result) =>
        result["result"]!["content"]![0]!["text"]!.GetValue<string>();

    private static bool IsError(JsonNode result) =>
        result["result"]!["isError"]!.GetValue<bool>();

    private static (DisassembleTool tool, FakeSession session) CreateTool()
    {
        var session = new FakeSession();
        session.SetupRequest("disassemble", JsonNode.Parse("""
            {
                "instructions": [
                    {"address": "0x00401000", "instruction": "push rbp", "symbol": "main"},
                    {"address": "0x00401001", "instruction": "mov rbp, rsp"},
                    {"address": "0x00401004", "instruction": "sub rsp, 0x20", "location": {"path": "/app/main.c", "name": "main.c"}, "line": 5}
                ]
            }
            """)!);
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<DisassembleTool>>();
        return (new DisassembleTool(registry, logger), session);
    }

    [TestMethod]
    public void Name_Is_disassemble()
    {
        var (tool, _) = CreateTool();
        tool.Name.Should().Be("disassemble");
    }

    [TestMethod]
    public void Description_Is_Not_Empty()
    {
        var (tool, _) = CreateTool();
        tool.Description.Should().NotBeNullOrWhiteSpace();
    }

    [TestMethod]
    public void InputSchema_Has_Required_Fields()
    {
        var (tool, _) = CreateTool();
        var schema = tool.GetInputSchema();
        var required = schema["required"] as JsonArray;
        required.Should().NotBeNull();
        var requiredNames = required!.Select(r => r!.GetValue<string>()).ToList();
        requiredNames.Should().Contain("sessionId");
        requiredNames.Should().Contain("memoryReference");
    }

    [TestMethod]
    public async Task Returns_Disassembled_Instructions()
    {
        var (tool, _) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1", "memoryReference":"0x00401000"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        var json = JsonNode.Parse(GetText(result))!;
        json["instructionCount"]!.GetValue<int>().Should().Be(3);

        var instructions = (json["instructions"] as JsonArray)!;
        instructions[0]!["address"]!.GetValue<string>().Should().Be("0x00401000");
        instructions[0]!["instruction"]!.GetValue<string>().Should().Be("push rbp");
        instructions[0]!["symbol"]!.GetValue<string>().Should().Be("main");

        // Third instruction should have source info
        instructions[2]!["source"]!.GetValue<string>().Should().Be("/app/main.c");
        instructions[2]!["line"]!.GetValue<int>().Should().Be(5);
    }

    [TestMethod]
    public async Task Sends_Disassemble_Dap_Request()
    {
        var (tool, session) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1", "memoryReference":"0x00401000", "instructionCount": 30}""");

        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        session.SentRequests.Should().Contain(r => r.Command == "disassemble");
        var sentArgs = session.SentRequests.First(r => r.Command == "disassemble").Args;
        sentArgs!["memoryReference"]!.GetValue<string>().Should().Be("0x00401000");
        sentArgs["instructionCount"]!.GetValue<int>().Should().Be(30);
        sentArgs["resolveSymbols"]!.GetValue<bool>().Should().BeTrue();
    }

    [TestMethod]
    public async Task Session_Not_Found_Returns_Error()
    {
        var registry = FakeSessionRegistry.Empty();
        var logger = Substitute.For<ILogger<DisassembleTool>>();
        var tool = new DisassembleTool(registry, logger);
        var args = JsonNode.Parse("""{"sessionId":"unknown", "memoryReference":"0x00401000"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
    }

    [TestMethod]
    public async Task Missing_MemoryReference_Returns_Error()
    {
        var (tool, _) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        result["error"].Should().NotBeNull();
        result["error"]!["message"]!.GetValue<string>().Should().Contain("memoryReference");
    }

    [TestMethod]
    public async Task Dap_Error_Returns_Error_Result()
    {
        var session = new FakeSession();
        session.SetupRequestError("disassemble", "Not supported");
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<DisassembleTool>>();
        var tool = new DisassembleTool(registry, logger);
        var args = JsonNode.Parse("""{"sessionId":"sess1", "memoryReference":"0x00401000"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("Not supported");
    }

    [TestMethod]
    public async Task Running_Session_Returns_Error()
    {
        var (tool, session) = CreateTool();
        session.State = SessionState.Running;
        var args = JsonNode.Parse("""{"sessionId":"sess1", "memoryReference":"0x00401000"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("running");
    }

    [TestMethod]
    public async Task Empty_Instructions_Returns_Message()
    {
        var session = new FakeSession();
        session.SetupRequest("disassemble", JsonNode.Parse("""{"instructions": []}""")!);
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<DisassembleTool>>();
        var tool = new DisassembleTool(registry, logger);
        var args = JsonNode.Parse("""{"sessionId":"sess1", "memoryReference":"0x00401000"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        var json = JsonNode.Parse(GetText(result))!;
        json["instructionCount"]!.GetValue<int>().Should().Be(0);
        json["message"].Should().NotBeNull();
    }

    [TestMethod]
    public async Task InstructionCount_Is_Clamped_To_Max_200()
    {
        var (tool, session) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1", "memoryReference":"0x00401000", "instructionCount": 999}""");

        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var sentArgs = session.SentRequests.First(r => r.Command == "disassemble").Args;
        sentArgs!["instructionCount"]!.GetValue<int>().Should().Be(200);
    }

    [TestMethod]
    public async Task InstructionCount_Is_Clamped_To_Min_1()
    {
        var (tool, session) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1", "memoryReference":"0x00401000", "instructionCount": 0}""");

        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var sentArgs = session.SentRequests.First(r => r.Command == "disassemble").Args;
        sentArgs!["instructionCount"]!.GetValue<int>().Should().Be(1);
    }

    [TestMethod]
    public async Task Default_InstructionCount_Is_20()
    {
        var (tool, session) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1", "memoryReference":"0x00401000"}""");

        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var sentArgs = session.SentRequests.First(r => r.Command == "disassemble").Args;
        sentArgs!["instructionCount"]!.GetValue<int>().Should().Be(20);
    }

    [TestMethod]
    public async Task Offset_Is_Passed_To_Dap()
    {
        var (tool, session) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1", "memoryReference":"0x00401000", "offset": 16}""");

        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var sentArgs = session.SentRequests.First(r => r.Command == "disassemble").Args;
        sentArgs!["offset"]!.GetValue<int>().Should().Be(16);
    }

    [TestMethod]
    public async Task InstructionOffset_Is_Passed_To_Dap()
    {
        var (tool, session) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1", "memoryReference":"0x00401000", "instructionOffset": -10}""");

        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var sentArgs = session.SentRequests.First(r => r.Command == "disassemble").Args;
        sentArgs!["instructionOffset"]!.GetValue<int>().Should().Be(-10);
    }

    [TestMethod]
    public async Task Instruction_Without_Symbol_Omits_Symbol()
    {
        var (tool, _) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1", "memoryReference":"0x00401000"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var json = JsonNode.Parse(GetText(result))!;
        var instructions = (json["instructions"] as JsonArray)!;
        // Second instruction has no symbol
        instructions[1]!["symbol"].Should().BeNull();
    }

    [TestMethod]
    public async Task Instruction_Without_Source_Omits_Source()
    {
        var (tool, _) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1", "memoryReference":"0x00401000"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var json = JsonNode.Parse(GetText(result))!;
        var instructions = (json["instructions"] as JsonArray)!;
        // First instruction has no location
        instructions[0]!["source"].Should().BeNull();
        instructions[0]!["line"].Should().BeNull();
    }

    [TestMethod]
    public async Task Null_Instruction_In_Array_Is_Skipped()
    {
        var session = new FakeSession();
        session.SetupRequest("disassemble", JsonNode.Parse("""
            {
                "instructions": [
                    {"address": "0x00401000", "instruction": "nop"},
                    null,
                    {"address": "0x00401001", "instruction": "ret"}
                ]
            }
            """)!);
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<DisassembleTool>>();
        var tool = new DisassembleTool(registry, logger);
        var args = JsonNode.Parse("""{"sessionId":"sess1", "memoryReference":"0x00401000"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        var json = JsonNode.Parse(GetText(result))!;
        json["instructionCount"]!.GetValue<int>().Should().Be(2);
    }

    [TestMethod]
    public async Task Missing_SessionId_Returns_Error()
    {
        var (tool, _) = CreateTool();
        var args = JsonNode.Parse("""{"memoryReference":"0x00401000"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        result["error"].Should().NotBeNull();
    }
}
