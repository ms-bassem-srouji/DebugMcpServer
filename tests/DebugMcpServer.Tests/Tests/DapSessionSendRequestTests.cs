using System.Text;
using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using DebugMcpServer.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DebugMcpServer.Tests.Tests;

[TestClass]
public class DapSessionSendRequestTests
{
    [TestMethod]
    public async Task SendRequest_WritesCorrectDapFormat()
    {
        var (session, adapterOutput, adapterInput) = DapSessionTestHelper.Create();
        session.StartReaderLoop();

        // Start request in background (will block waiting for response)
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var requestTask = Task.Run(() => session.SendRequestAsync("test", new { foo = "bar" }, cts.Token));

        await Task.Delay(100);

        // Read what was written to the adapter's stdin
        adapterInput.Position = 0;
        var message = await DapSession.ReadDapMessageAsync(adapterInput, CancellationToken.None);

        message.Should().NotBeNull();
        message!["type"]!.GetValue<string>().Should().Be("request");
        message["command"]!.GetValue<string>().Should().Be("test");
        message["arguments"]!["foo"]!.GetValue<string>().Should().Be("bar");
        message["seq"]!.GetValue<int>().Should().BeGreaterThan(0);

        // Clean up - send a response to unblock
        var bytes = DapSessionTestHelper.FormatDapMessage(
            $$$"""{"type":"response","request_seq":{{{message["seq"]!.GetValue<int>()}}},"command":"test","success":true,"body":{}}""");
        adapterOutput.Write(bytes, 0, bytes.Length);

        await requestTask.WaitAsync(TimeSpan.FromSeconds(5));
        session.Dispose();
    }

    [TestMethod]
    public async Task SendRequest_SeqIncrements()
    {
        var (session, adapterOutput, adapterInput) = DapSessionTestHelper.Create();
        session.StartReaderLoop();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        // Send two requests
        var task1 = Task.Run(() => session.SendRequestAsync("cmd1", null, cts.Token));
        await Task.Delay(50);
        var task2 = Task.Run(() => session.SendRequestAsync("cmd2", null, cts.Token));
        await Task.Delay(100);

        // Read both messages
        adapterInput.Position = 0;
        var msg1 = await DapSession.ReadDapMessageAsync(adapterInput, CancellationToken.None);
        var msg2 = await DapSession.ReadDapMessageAsync(adapterInput, CancellationToken.None);

        msg1.Should().NotBeNull();
        msg2.Should().NotBeNull();
        var seq1 = msg1!["seq"]!.GetValue<int>();
        var seq2 = msg2!["seq"]!.GetValue<int>();
        seq2.Should().BeGreaterThan(seq1);

        // Resolve both
        var r1 = DapSessionTestHelper.FormatDapMessage(
            $$$"""{"type":"response","request_seq":{{{seq1}}},"command":"cmd1","success":true,"body":{}}""");
        var r2 = DapSessionTestHelper.FormatDapMessage(
            $$$"""{"type":"response","request_seq":{{{seq2}}},"command":"cmd2","success":true,"body":{}}""");
        adapterOutput.Write(r1, 0, r1.Length);
        adapterOutput.Write(r2, 0, r2.Length);

        await Task.WhenAll(task1, task2).WaitAsync(TimeSpan.FromSeconds(5));
        session.Dispose();
    }

    [TestMethod]
    public async Task SendRequest_NullArgs_OmitsArgumentsField()
    {
        var (session, adapterOutput, adapterInput) = DapSessionTestHelper.Create();
        session.StartReaderLoop();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var requestTask = Task.Run(() => session.SendRequestAsync("no_args", null, cts.Token));
        await Task.Delay(100);

        adapterInput.Position = 0;
        var message = await DapSession.ReadDapMessageAsync(adapterInput, CancellationToken.None);

        message.Should().NotBeNull();
        message!["arguments"].Should().BeNull();

        // Resolve
        var seq = message["seq"]!.GetValue<int>();
        var r = DapSessionTestHelper.FormatDapMessage(
            $$$"""{"type":"response","request_seq":{{{seq}}},"command":"no_args","success":true,"body":{}}""");
        adapterOutput.Write(r, 0, r.Length);

        await requestTask.WaitAsync(TimeSpan.FromSeconds(5));
        session.Dispose();
    }

    [TestMethod]
    public async Task SendRequest_Cancellation_ThrowsAndCleansUpWaiter()
    {
        var (session, adapterOutput, _) = DapSessionTestHelper.Create();
        session.StartReaderLoop();

        var cts = new CancellationTokenSource();
        var requestTask = Task.Run(() => session.SendRequestAsync("slow", null, cts.Token));
        await Task.Delay(100);

        cts.Cancel();

        var act = async () => await requestTask.WaitAsync(TimeSpan.FromSeconds(5));
        await act.Should().ThrowAsync<OperationCanceledException>();

        session.Dispose();
    }

    [TestMethod]
    public async Task SendRequest_WithArgs_SerializesCorrectly()
    {
        var (session, adapterOutput, adapterInput) = DapSessionTestHelper.Create();
        session.StartReaderLoop();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var requestTask = Task.Run(() => session.SendRequestAsync("setBreakpoints", new
        {
            source = new { path = "/app/main.cs" },
            breakpoints = new[] { new { line = 42 } }
        }, cts.Token));

        await Task.Delay(100);

        adapterInput.Position = 0;
        var message = await DapSession.ReadDapMessageAsync(adapterInput, CancellationToken.None);

        message.Should().NotBeNull();
        message!["arguments"]!["source"]!["path"]!.GetValue<string>().Should().Be("/app/main.cs");
        (message["arguments"]!["breakpoints"] as JsonArray)!.Count.Should().Be(1);

        // Resolve
        var seq = message["seq"]!.GetValue<int>();
        var r = DapSessionTestHelper.FormatDapMessage(
            $$$"""{"type":"response","request_seq":{{{seq}}},"command":"setBreakpoints","success":true,"body":{}}""");
        adapterOutput.Write(r, 0, r.Length);

        await requestTask.WaitAsync(TimeSpan.FromSeconds(5));
        session.Dispose();
    }

    [TestMethod]
    public async Task SendRequest_DapFailure_ThrowsDapSessionException()
    {
        var (session, adapterOutput, adapterInput) = DapSessionTestHelper.Create();
        session.StartReaderLoop();

        var requestTask = Task.Run(() => session.SendRequestAsync("bad_cmd", null, CancellationToken.None));
        await Task.Delay(100);

        adapterInput.Position = 0;
        var message = await DapSession.ReadDapMessageAsync(adapterInput, CancellationToken.None);
        var seq = message!["seq"]!.GetValue<int>();

        var r = DapSessionTestHelper.FormatDapMessage(
            $$$"""{"type":"response","request_seq":{{{seq}}},"command":"bad_cmd","success":false,"message":"Access denied"}""");
        adapterOutput.Write(r, 0, r.Length);

        var act = async () => await requestTask.WaitAsync(TimeSpan.FromSeconds(5));
        await act.Should().ThrowAsync<DapSessionException>().WithMessage("Access denied");

        session.Dispose();
    }

    [TestMethod]
    public async Task SendRequest_ConcurrentRequests_AllResolveCorrectly()
    {
        var (session, adapterOutput, adapterInput) = DapSessionTestHelper.Create();
        session.StartReaderLoop();

        // Fire 3 concurrent requests
        var task1 = Task.Run(() => session.SendRequestAsync("cmd1", null, CancellationToken.None));
        var task2 = Task.Run(() => session.SendRequestAsync("cmd2", null, CancellationToken.None));
        var task3 = Task.Run(() => session.SendRequestAsync("cmd3", null, CancellationToken.None));

        await Task.Delay(200);

        // Read all 3 requests
        adapterInput.Position = 0;
        var messages = new List<JsonNode>();
        for (int i = 0; i < 3; i++)
        {
            var msg = await DapSession.ReadDapMessageAsync(adapterInput, CancellationToken.None);
            msg.Should().NotBeNull();
            messages.Add(msg!);
        }

        // Respond to all in reverse order
        foreach (var msg in messages.AsEnumerable().Reverse())
        {
            var seq = msg["seq"]!.GetValue<int>();
            var cmd = msg["command"]!.GetValue<string>();
            var r = DapSessionTestHelper.FormatDapMessage(
                $$$"""{"type":"response","request_seq":{{{seq}}},"command":"{{{cmd}}}","success":true,"body":{"cmd":"{{{cmd}}}"}}""");
            adapterOutput.Write(r, 0, r.Length);
        }

        var results = await Task.WhenAll(task1, task2, task3).WaitAsync(TimeSpan.FromSeconds(5));
        results.Should().HaveCount(3);

        session.Dispose();
    }
}
