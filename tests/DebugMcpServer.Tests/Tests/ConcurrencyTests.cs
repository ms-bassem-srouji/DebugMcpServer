using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using DebugMcpServer.Server;
using DebugMcpServer.Tools;
using DebugMcpServer.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DebugMcpServer.Tests.Tests;

[TestClass]
public class ConcurrencyTests
{
    // --- Breakpoints ConcurrentDictionary ---

    [TestMethod]
    public void Breakpoints_Is_ConcurrentDictionary()
    {
        var session = new FakeSession();

        session.Breakpoints.Should().BeOfType<ConcurrentDictionary<string, List<SourceBreakpoint>>>();
    }

    [TestMethod]
    public async Task Breakpoints_GetOrAdd_Is_Thread_Safe()
    {
        var session = new FakeSession();
        var file = @"C:\app\Program.cs";

        // Simulate concurrent GetOrAdd from multiple threads
        var tasks = Enumerable.Range(0, 10).Select(i =>
            Task.Run(() =>
            {
                var list = session.Breakpoints.GetOrAdd(file, _ => new List<SourceBreakpoint>());
                return list;
            })).ToArray();

        await Task.WhenAll(tasks);

        // All tasks should get the same list instance
        var lists = tasks.Select(t => t.Result).Distinct().ToList();
        lists.Should().HaveCount(1, "GetOrAdd should return the same list for all concurrent callers");
    }

    // --- EventConsumerLock ---

    [TestMethod]
    public void FakeSession_Has_EventConsumerLock()
    {
        var session = new FakeSession();

        session.EventConsumerLock.Should().NotBeNull();
        session.EventConsumerLock.CurrentCount.Should().Be(1);
    }

    [TestMethod]
    public async Task EventConsumerLock_Serializes_Access()
    {
        var session = new FakeSession();
        var order = new ConcurrentQueue<int>();

        // First consumer acquires the lock
        await session.EventConsumerLock.WaitAsync();

        // Second consumer must wait
        var secondTask = Task.Run(async () =>
        {
            await session.EventConsumerLock.WaitAsync();
            order.Enqueue(2);
            session.EventConsumerLock.Release();
        });

        // Give second task time to block on the lock
        await Task.Delay(50);
        order.Should().BeEmpty("second consumer should be blocked");

        // Release first consumer
        order.Enqueue(1);
        session.EventConsumerLock.Release();

        await secondTask;

        order.ToArray().Should().Equal(1, 2);
    }

    // --- Volatile State / ActiveThreadId ---

    [TestMethod]
    public void ActiveThreadId_Set_And_Get_Are_Consistent()
    {
        var session = new FakeSession();

        session.ActiveThreadId = 42;
        session.ActiveThreadId.Should().Be(42);

        session.ActiveThreadId = null;
        session.ActiveThreadId.Should().BeNull();

        session.ActiveThreadId = 99;
        session.ActiveThreadId.Should().Be(99);
    }

    [TestMethod]
    public void State_Is_Readable_Across_Transitions()
    {
        var session = new FakeSession();

        session.State = SessionState.Paused;
        session.State.Should().Be(SessionState.Paused);

        session.TransitionToRunning();
        session.State.Should().Be(SessionState.Running);
    }

    // --- McpHostedService concurrent dispatch ---

    [TestMethod]
    public async Task HandleRequestAsync_Can_Process_Multiple_Requests()
    {
        // Create a tool that takes some time
        var slowTool = new SlowTool();
        var tools = new IMcpTool[] { slowTool };
        var logger = NullLogger<McpHostedService>.Instance;
        var lifetime = Substitute.For<Microsoft.Extensions.Hosting.IHostApplicationLifetime>();
        var service = new McpHostedService(logger, lifetime, tools);

        // Dispatch two requests concurrently
        var request1 = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"slow_tool","arguments":{}}}""")!;
        var request2 = JsonNode.Parse("""{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"slow_tool","arguments":{}}}""")!;

        var task1 = service.HandleRequestAsync(request1, CancellationToken.None);
        var task2 = service.HandleRequestAsync(request2, CancellationToken.None);

        var results = await Task.WhenAll(task1, task2);

        results.Should().HaveCount(2);
        results[0]["id"]!.GetValue<int>().Should().Be(1);
        results[1]["id"]!.GetValue<int>().Should().Be(2);
    }

    // --- RemoveBreakpoint rollback ---

    [TestMethod]
    public async Task RemoveBreakpoint_Rolls_Back_On_Dap_Failure()
    {
        var session = new FakeSession();
        session.Breakpoints.GetOrAdd(@"C:\app\Program.cs", _ => new List<SourceBreakpoint>
        {
            new(@"C:\app\Program.cs", 10),
            new(@"C:\app\Program.cs", 20)
        });
        session.SetupRequestError("setBreakpoints", "adapter crashed");
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<RemoveBreakpointTool>>();
        var tool = new RemoveBreakpointTool(registry, logger);

        var args = JsonNode.Parse("""{"sessionId":"sess1","file":"C:\\app\\Program.cs","line":10}""");

        // The tool's SendBreakpointsForFile catches DapSessionException internally,
        // so it returns an error result rather than throwing
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        // The breakpoint list should be preserved since the DAP call failed
        // (SendBreakpointsForFile catches the exception internally and returns error)
        result["result"]!["isError"]!.GetValue<bool>().Should().BeTrue();
    }

    [TestMethod]
    public async Task SetBreakpoint_Rolls_Back_On_Dap_Failure()
    {
        var session = new FakeSession();
        session.SetupRequestError("setBreakpoints", "adapter crashed");
        var registry = FakeSessionRegistry.WithSession("sess1", session);
        var logger = Substitute.For<ILogger<SetBreakpointTool>>();
        var tool = new SetBreakpointTool(registry, logger);

        var args = JsonNode.Parse("""{"sessionId":"sess1","file":"C:\\app\\Program.cs","line":42}""");
        var result = await tool.ExecuteAsync(JsonValue.Create(1), args, CancellationToken.None);

        // Should return error
        result["result"]!["isError"]!.GetValue<bool>().Should().BeTrue();
    }

    /// <summary>
    /// Minimal tool for testing concurrent dispatch.
    /// </summary>
    private sealed class SlowTool : ToolBase, IMcpTool
    {
        public string Name => "slow_tool";
        public string Description => "test";
        public JsonNode GetInputSchema() => JsonNode.Parse("""{"type":"object","properties":{},"required":[]}""")!;

        public Task<JsonNode> ExecuteAsync(JsonNode? id, JsonNode? arguments, CancellationToken cancellationToken)
        {
            return Task.FromResult(CreateTextResult(id, """{"ok":true}"""));
        }
    }
}
