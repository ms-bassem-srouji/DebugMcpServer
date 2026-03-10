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
public class RemoveBreakpointToolTests
{
    private static string GetText(JsonNode result) =>
        result["result"]!["content"]![0]!["text"]!.GetValue<string>();

    private static bool IsError(JsonNode result) =>
        result["result"]!["isError"]!.GetValue<bool>();

    private static (RemoveBreakpointTool tool, FakeSession session) CreateTool()
    {
        var session = new FakeSession();
        session.SetupRequest("setBreakpoints", JsonNode.Parse("""{"breakpoints":[]}""")!);
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<RemoveBreakpointTool>>();
        return (new RemoveBreakpointTool(registry, logger), session);
    }

    [TestMethod]
    public async Task Removes_Breakpoint_From_Session_Dict()
    {
        var (tool, session) = CreateTool();
        session.Breakpoints[@"C:\app\Program.cs"] = new List<SourceBreakpoint>
        {
            new(@"C:\app\Program.cs", 42),
            new(@"C:\app\Program.cs", 100)
        };

        var args = JsonNode.Parse("""{"sessionId":"sess1","file":"C:\\app\\Program.cs","line":42}""");
        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        session.Breakpoints[@"C:\app\Program.cs"].Should().HaveCount(1);
        session.Breakpoints[@"C:\app\Program.cs"].Should().NotContain(b => b.Line == 42);
        session.Breakpoints[@"C:\app\Program.cs"].Should().Contain(b => b.Line == 100);
    }

    [TestMethod]
    public async Task Sends_SetBreakpoints_With_Updated_List()
    {
        var (tool, session) = CreateTool();
        session.Breakpoints[@"C:\app\Program.cs"] = new List<SourceBreakpoint>
        {
            new(@"C:\app\Program.cs", 42)
        };

        var args = JsonNode.Parse("""{"sessionId":"sess1","file":"C:\\app\\Program.cs","line":42}""");
        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        session.SentRequests.Should().Contain(r => r.Command == "setBreakpoints");
        session.Breakpoints[@"C:\app\Program.cs"].Should().BeEmpty();
    }

    [TestMethod]
    public async Task Does_Nothing_If_Breakpoint_Not_Found()
    {
        var (tool, session) = CreateTool();

        var args = JsonNode.Parse("""{"sessionId":"sess1","file":"C:\\app\\Program.cs","line":99}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        session.SentRequests.Should().Contain(r => r.Command == "setBreakpoints");
    }

    [TestMethod]
    public void Name_Is_remove_breakpoint()
    {
        var (tool, _) = CreateTool();
        tool.Name.Should().Be("remove_breakpoint");
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
        var required = tool.GetInputSchema()["required"] as JsonArray;
        var names = required!.Select(r => r!.GetValue<string>()).ToList();
        names.Should().Contain("sessionId");
    }

    [TestMethod]
    public void InputSchema_Has_Expected_Properties()
    {
        var (tool, _) = CreateTool();
        var props = tool.GetInputSchema()["properties"] as JsonObject;
        props!.ContainsKey("sessionId").Should().BeTrue();
        props.ContainsKey("file").Should().BeTrue();
        props.ContainsKey("line").Should().BeTrue();
        props.ContainsKey("all").Should().BeTrue();
        props.ContainsKey("allFunctions").Should().BeTrue();
    }

    [TestMethod]
    public async Task Missing_SessionId_Returns_Error()
    {
        var (tool, _) = CreateTool();
        var args = JsonNode.Parse("""{"file":"C:\\app\\Program.cs","line":42}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        result["error"].Should().NotBeNull();
    }

    [TestMethod]
    public async Task Missing_File_And_No_Flags_Returns_Error()
    {
        var (tool, _) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1","line":42}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        result["error"].Should().NotBeNull();
    }

    [TestMethod]
    public async Task File_Without_Line_Removes_All_In_File()
    {
        var (tool, session) = CreateTool();
        session.Breakpoints[@"C:\app\Program.cs"] = new List<SourceBreakpoint>
        {
            new(@"C:\app\Program.cs", 10),
            new(@"C:\app\Program.cs", 20)
        };

        var args = JsonNode.Parse("""{"sessionId":"sess1","file":"C:\\app\\Program.cs"}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        session.Breakpoints[@"C:\app\Program.cs"].Should().BeEmpty();
        session.SentRequests.Should().Contain(r => r.Command == "setBreakpoints");
    }

    [TestMethod]
    public async Task Session_Not_Found_Returns_Error()
    {
        var session = new FakeSession();
        var registry = FakeSessionRegistry.Empty();
        var logger = Substitute.For<ILogger<RemoveBreakpointTool>>();
        var tool = new RemoveBreakpointTool(registry, logger);

        var args = JsonNode.Parse("""{"sessionId":"nonexistent","file":"C:\\app\\Program.cs","line":42}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("not found");
    }

    [TestMethod]
    public async Task Null_Arguments_Returns_Error()
    {
        var (tool, _) = CreateTool();

        var result = await tool.ExecuteAsync(JsonValue.Create(1), null, CancellationToken.None);
        result["error"].Should().NotBeNull();
    }

    [TestMethod]
    public async Task Multiple_Breakpoints_Only_Target_Removed()
    {
        var (tool, session) = CreateTool();
        session.Breakpoints[@"C:\app\Program.cs"] = new List<SourceBreakpoint>
        {
            new(@"C:\app\Program.cs", 10),
            new(@"C:\app\Program.cs", 20),
            new(@"C:\app\Program.cs", 30)
        };

        var args = JsonNode.Parse("""{"sessionId":"sess1","file":"C:\\app\\Program.cs","line":20}""");
        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        session.Breakpoints[@"C:\app\Program.cs"].Should().HaveCount(2);
        session.Breakpoints[@"C:\app\Program.cs"].Should().Contain(b => b.Line == 10);
        session.Breakpoints[@"C:\app\Program.cs"].Should().Contain(b => b.Line == 30);
        session.Breakpoints[@"C:\app\Program.cs"].Should().NotContain(b => b.Line == 20);
    }

    [TestMethod]
    public async Task Remove_From_File_With_No_Breakpoints_Still_Sends_Request()
    {
        var (tool, session) = CreateTool();
        // No breakpoints in the dict for this file

        var args = JsonNode.Parse("""{"sessionId":"sess1","file":"C:\\app\\Other.cs","line":5}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        session.SentRequests.Should().Contain(r => r.Command == "setBreakpoints");
    }

    [TestMethod]
    public async Task Remove_Last_Breakpoint_Leaves_Empty_List()
    {
        var (tool, session) = CreateTool();
        session.Breakpoints[@"C:\app\Program.cs"] = new List<SourceBreakpoint>
        {
            new(@"C:\app\Program.cs", 42)
        };

        var args = JsonNode.Parse("""{"sessionId":"sess1","file":"C:\\app\\Program.cs","line":42}""");
        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        session.Breakpoints[@"C:\app\Program.cs"].Should().BeEmpty();
    }

    [TestMethod]
    public async Task All_True_Removes_All_Source_Breakpoints_Across_Files()
    {
        var (tool, session) = CreateTool();
        session.Breakpoints[@"C:\app\Program.cs"] = new List<SourceBreakpoint>
        {
            new(@"C:\app\Program.cs", 10),
            new(@"C:\app\Program.cs", 20)
        };
        session.Breakpoints[@"C:\app\Other.cs"] = new List<SourceBreakpoint>
        {
            new(@"C:\app\Other.cs", 5)
        };

        var args = JsonNode.Parse("""{"sessionId":"sess1","all":true}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        GetText(result).Should().Contain("Removed all source breakpoints");
        session.Breakpoints.Should().BeEmpty();
        session.SentRequests.Where(r => r.Command == "setBreakpoints").Should().HaveCount(2);
    }

    [TestMethod]
    public async Task All_True_With_No_Breakpoints_Returns_Success()
    {
        var (tool, session) = CreateTool();

        var args = JsonNode.Parse("""{"sessionId":"sess1","all":true}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        GetText(result).Should().Contain("0 file(s)");
    }

    [TestMethod]
    public async Task AllFunctions_True_Sends_Empty_SetFunctionBreakpoints()
    {
        var (tool, session) = CreateTool();
        session.SetupRequest("setFunctionBreakpoints", JsonNode.Parse("""{"breakpoints":[]}""")!);

        var args = JsonNode.Parse("""{"sessionId":"sess1","allFunctions":true}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeFalse();
        GetText(result).Should().Contain("Cleared all function breakpoints");
        session.SentRequests.Should().Contain(r => r.Command == "setFunctionBreakpoints");
    }

    [TestMethod]
    public async Task AllFunctions_DapError_Returns_Error()
    {
        var (tool, session) = CreateTool();
        session.SetupRequestError("setFunctionBreakpoints", "unsupported");

        var args = JsonNode.Parse("""{"sessionId":"sess1","allFunctions":true}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        IsError(result).Should().BeTrue();
        GetText(result).Should().Contain("DAP error");
    }

    [TestMethod]
    public async Task Remove_All_In_File_Does_Not_Affect_Other_Files()
    {
        var (tool, session) = CreateTool();
        session.Breakpoints[@"C:\app\Program.cs"] = new List<SourceBreakpoint>
        {
            new(@"C:\app\Program.cs", 10)
        };
        session.Breakpoints[@"C:\app\Other.cs"] = new List<SourceBreakpoint>
        {
            new(@"C:\app\Other.cs", 5)
        };

        var args = JsonNode.Parse("""{"sessionId":"sess1","file":"C:\\app\\Program.cs"}""");
        await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        session.Breakpoints[@"C:\app\Program.cs"].Should().BeEmpty();
        session.Breakpoints[@"C:\app\Other.cs"].Should().HaveCount(1);
    }

    [TestMethod]
    public async Task No_File_No_Flags_Returns_Error_With_Guidance()
    {
        var (tool, _) = CreateTool();
        var args = JsonNode.Parse("""{"sessionId":"sess1"}""");

        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);
        result["error"].Should().NotBeNull();
    }
}
