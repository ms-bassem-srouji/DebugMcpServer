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
}
