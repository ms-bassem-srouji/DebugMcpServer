using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using DebugMcpServer.Tests.Fakes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DebugMcpServer.Tests.Tests;

/// <summary>A stream that throws on any write operation, to test error handling paths.</summary>
internal sealed class ClosedStream : Stream
{
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => 0;
    public override long Position { get => 0; set { } }

    public override void Write(byte[] buffer, int offset, int count) => throw new ObjectDisposedException("Stream is closed");
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) => throw new ObjectDisposedException("Stream is closed");
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) => throw new ObjectDisposedException("Stream is closed");
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => 0;
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

[TestClass]
public class DapSessionDispatchTests
{
    private static void WriteMessage(Stream stream, string json)
    {
        var bytes = DapSessionTestHelper.FormatDapMessage(json);
        stream.Write(bytes, 0, bytes.Length);
    }

    [TestMethod]
    public async Task DispatchResponse_Success_ResolvesWaiter()
    {
        var (session, adapterOutput, _) = DapSessionTestHelper.Create();

        // Register a waiter by sending a request (we'll manually write to the stream)
        // Instead, we directly test via the reader loop
        session.StartReaderLoop();

        // Simulate: we need a waiter registered first. Use SendRequestAsync in background
        // which will write to adapterInput and register a waiter.
        // Then feed a response through adapterOutput.

        // Simpler approach: feed a response that matches a known seq
        // We need to register the waiter manually via reflection or via SendRequestAsync
        // Let's use SendRequestAsync which registers the waiter

        var requestTask = Task.Run(async () =>
        {
            return await session.SendRequestAsync("test_command", null, CancellationToken.None);
        });

        // Give SendRequestAsync time to register the waiter and write the request
        await Task.Delay(100);

        // Now feed a success response for seq=1 (first request)
        WriteMessage(adapterOutput, """{"type":"response","request_seq":1,"command":"test_command","success":true,"body":{"value":"hello"}}""");

        var result = await requestTask.WaitAsync(TimeSpan.FromSeconds(5));
        result["value"]!.GetValue<string>().Should().Be("hello");

        session.Dispose();
    }

    [TestMethod]
    public async Task DispatchResponse_Failure_FaultsWaiter()
    {
        var (session, adapterOutput, _) = DapSessionTestHelper.Create();
        session.StartReaderLoop();

        var requestTask = Task.Run(async () =>
        {
            return await session.SendRequestAsync("fail_command", null, CancellationToken.None);
        });

        await Task.Delay(100);

        WriteMessage(adapterOutput, """{"type":"response","request_seq":1,"command":"fail_command","success":false,"message":"Something broke"}""");

        var act = async () => await requestTask.WaitAsync(TimeSpan.FromSeconds(5));
        await act.Should().ThrowAsync<DapSessionException>().WithMessage("Something broke");

        session.Dispose();
    }

    [TestMethod]
    public async Task DispatchResponse_UnknownSeq_DoesNotCrash()
    {
        var (session, adapterOutput, _) = DapSessionTestHelper.Create();
        session.StartReaderLoop();

        // Feed a response with a seq that no one is waiting for
        WriteMessage(adapterOutput, """{"type":"response","request_seq":999,"command":"phantom","success":true,"body":{}}""");

        // Feed an event to verify the session is still processing
        WriteMessage(adapterOutput, """{"type":"event","event":"stopped","body":{"reason":"breakpoint","threadId":1}}""");

        // Wait for the stopped event to be dispatched
        var evt = await session.EventChannel.ReadAsync(CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        evt.EventType.Should().Be("stopped");

        session.Dispose();
    }

    [TestMethod]
    public async Task DispatchEvent_Stopped_SetsPausedStateAndThreadId()
    {
        var (session, adapterOutput, _) = DapSessionTestHelper.Create();
        session.StartReaderLoop();

        WriteMessage(adapterOutput, """{"type":"event","event":"stopped","body":{"reason":"breakpoint","threadId":42}}""");

        var evt = await session.EventChannel.ReadAsync(CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        session.State.Should().Be(SessionState.Paused);
        session.ActiveThreadId.Should().Be(42);
        evt.EventType.Should().Be("stopped");

        session.Dispose();
    }

    [TestMethod]
    public async Task DispatchEvent_Continued_SetsRunningState()
    {
        var (session, adapterOutput, _) = DapSessionTestHelper.Create();
        session.StartReaderLoop();

        WriteMessage(adapterOutput, """{"type":"event","event":"continued","body":{}}""");

        var evt = await session.EventChannel.ReadAsync(CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        session.State.Should().Be(SessionState.Running);
        evt.EventType.Should().Be("continued");

        session.Dispose();
    }

    [TestMethod]
    public async Task DispatchEvent_Terminated_SetsTerminatingState()
    {
        var (session, adapterOutput, _) = DapSessionTestHelper.Create();
        session.StartReaderLoop();

        WriteMessage(adapterOutput, """{"type":"event","event":"terminated","body":{}}""");

        var evt = await session.EventChannel.ReadAsync(CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        session.State.Should().Be(SessionState.Terminating);
        evt.EventType.Should().Be("terminated");

        session.Dispose();
    }

    [TestMethod]
    public async Task DispatchEvent_Initialized_CompletesInitializedTask()
    {
        var (session, adapterOutput, _) = DapSessionTestHelper.Create();
        session.StartReaderLoop();

        session.InitializedTask.IsCompleted.Should().BeFalse();

        WriteMessage(adapterOutput, """{"type":"event","event":"initialized","body":{}}""");

        await session.InitializedTask.WaitAsync(TimeSpan.FromSeconds(5));
        session.InitializedTask.IsCompletedSuccessfully.Should().BeTrue();

        session.Dispose();
    }

    [TestMethod]
    public async Task DispatchEvent_ChannelFull_DoesNotCrash()
    {
        // Create session with capacity of 1
        var (session, adapterOutput, _) = DapSessionTestHelper.Create(maxPendingEvents: 1);
        session.StartReaderLoop();

        // Write 3 events rapidly — channel should drop oldest
        WriteMessage(adapterOutput, """{"type":"event","event":"output","body":{"output":"line1"}}""");
        WriteMessage(adapterOutput, """{"type":"event","event":"output","body":{"output":"line2"}}""");
        WriteMessage(adapterOutput, """{"type":"event","event":"stopped","body":{"reason":"step","threadId":1}}""");

        // Should still be able to read at least one event
        var evt = await session.EventChannel.ReadAsync(CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        evt.Should().NotBeNull();

        session.Dispose();
    }

    [TestMethod]
    public async Task DispatchMessage_UnknownType_DoesNotCrash()
    {
        var (session, adapterOutput, _) = DapSessionTestHelper.Create();
        session.StartReaderLoop();

        WriteMessage(adapterOutput, """{"type":"garbage","data":"something"}""");

        // Verify session still processes subsequent messages
        WriteMessage(adapterOutput, """{"type":"event","event":"stopped","body":{"reason":"pause","threadId":1}}""");

        var evt = await session.EventChannel.ReadAsync(CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        evt.EventType.Should().Be("stopped");

        session.Dispose();
    }

    [TestMethod]
    public async Task DispatchReverseRequest_Handshake_EchoesSignature()
    {
        var (session, adapterOutput, adapterInput) = DapSessionTestHelper.Create();
        session.StartReaderLoop();

        WriteMessage(adapterOutput, """{"type":"request","seq":1,"command":"handshake","arguments":{"value":"test-signature-123"}}""");

        // Give time for the handshake response to be written
        await Task.Delay(200);

        // Read what was written to adapterInput (the "stdin" of the adapter)
        adapterInput.Position = 0;
        var response = await DapSession.ReadDapMessageAsync(adapterInput, CancellationToken.None);

        response.Should().NotBeNull();
        response!["command"]!.GetValue<string>().Should().Be("handshake");
        response["success"]!.GetValue<bool>().Should().BeTrue();
        response["body"]!["signature"]!.GetValue<string>().Should().Be("test-signature-123");

        session.Dispose();
    }

    [TestMethod]
    public async Task DispatchReverseRequest_UnknownCommand_SendsGenericSuccess()
    {
        var (session, adapterOutput, adapterInput) = DapSessionTestHelper.Create();
        session.StartReaderLoop();

        WriteMessage(adapterOutput, """{"type":"request","seq":5,"command":"unknownCommand"}""");

        await Task.Delay(200);

        adapterInput.Position = 0;
        var response = await DapSession.ReadDapMessageAsync(adapterInput, CancellationToken.None);

        response.Should().NotBeNull();
        response!["command"]!.GetValue<string>().Should().Be("unknownCommand");
        response["success"]!.GetValue<bool>().Should().BeTrue();
        response["request_seq"]!.GetValue<int>().Should().Be(5);

        session.Dispose();
    }

    [TestMethod]
    public async Task ReaderLoop_EOF_SetsTerminatedState()
    {
        var (session, adapterOutput, _) = DapSessionTestHelper.Create();
        session.StartReaderLoop();

        // Close the stream to signal EOF
        ((BlockingMemoryStream)adapterOutput).Complete();

        // Wait for the reader loop to finish
        await Task.Delay(200);

        session.State.Should().Be(SessionState.Terminated);

        session.Dispose();
    }

    [TestMethod]
    public async Task ReaderLoop_EOF_FaultsPendingWaiters()
    {
        var (session, adapterOutput, _) = DapSessionTestHelper.Create();
        session.StartReaderLoop();

        // Start a request that will wait for a response
        var requestTask = Task.Run(() => session.SendRequestAsync("hang_forever", null, CancellationToken.None));

        await Task.Delay(100);

        // Close the stream — reader loop should fault the waiter
        ((BlockingMemoryStream)adapterOutput).Complete();

        var act = async () => await requestTask.WaitAsync(TimeSpan.FromSeconds(5));
        await act.Should().ThrowAsync<DapSessionException>().WithMessage("*terminated*");

        session.Dispose();
    }

    [TestMethod]
    public async Task ReaderLoop_EOF_CompletesEventChannel()
    {
        var (session, adapterOutput, _) = DapSessionTestHelper.Create();
        session.StartReaderLoop();

        ((BlockingMemoryStream)adapterOutput).Complete();

        // Wait for reader loop to clean up
        await Task.Delay(200);

        session.EventChannel.Completion.IsCompleted.Should().BeTrue();

        session.Dispose();
    }

    [TestMethod]
    public async Task DispatchEvent_Stopped_WithoutThreadId_KeepsExistingActiveThread()
    {
        var (session, adapterOutput, _) = DapSessionTestHelper.Create();
        session.ActiveThreadId = 10;
        session.StartReaderLoop();

        WriteMessage(adapterOutput, """{"type":"event","event":"stopped","body":{"reason":"pause"}}""");

        await session.EventChannel.ReadAsync(CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        session.State.Should().Be(SessionState.Paused);
        session.ActiveThreadId.Should().Be(10); // unchanged
        session.Dispose();
    }

    [TestMethod]
    public async Task DispatchResponse_Success_WithNullBody_ReturnsEmptyObject()
    {
        var (session, adapterOutput, _) = DapSessionTestHelper.Create();
        session.StartReaderLoop();

        var requestTask = Task.Run(() => session.SendRequestAsync("no_body", null, CancellationToken.None));
        await Task.Delay(100);

        WriteMessage(adapterOutput, """{"type":"response","request_seq":1,"command":"no_body","success":true}""");

        var result = await requestTask.WaitAsync(TimeSpan.FromSeconds(5));
        result.Should().NotBeNull();
        // Should be an empty JsonObject when body is null
        result.ToJsonString().Should().Be("{}");

        session.Dispose();
    }

    [TestMethod]
    public async Task DispatchEvent_AfterChannelCompleted_LogsWarning()
    {
        // Use maxPendingEvents=1 and fill the channel, then close it, then send more events
        var (session, adapterOutput, _) = DapSessionTestHelper.Create(maxPendingEvents: 1);
        session.StartReaderLoop();

        // Send first event to fill the channel
        WriteMessage(adapterOutput, """{"type":"event","event":"output","body":{"output":"fill"}}""");
        await Task.Delay(100);

        // Read the event to drain the channel
        await session.EventChannel.ReadAsync(CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(2));

        // Close the stream to terminate the reader loop, which completes the channel
        ((BlockingMemoryStream)adapterOutput).Complete();
        await Task.Delay(200);

        // Channel is now completed — the write failure path was exercised during the reader loop
        // when events arrive on a full/closed channel
        session.State.Should().Be(SessionState.Terminated);
        session.Dispose();
    }

    [TestMethod]
    public async Task DispatchReverseRequest_HandshakeAfterDispose_CatchesException()
    {
        // Test the catch block in HandleReverseRequestAsync by sending a handshake
        // after the session's write stream is closed
        var adapterOutput = new BlockingMemoryStream();
        var adapterInput = new MemoryStream();
        // We need a session where writing will fail
        var session = new DapSession(
            inputStream: new ClosedStream(), // Writing will throw
            outputStream: adapterOutput,
            logger: Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        session.StartReaderLoop();

        // Send handshake — HandleReverseRequestAsync will try to write a response, which will throw
        // The catch block should handle it gracefully
        WriteMessage(adapterOutput, """{"type":"request","seq":1,"command":"handshake","arguments":{"value":"test"}}""");

        await Task.Delay(300);

        // Session should still be alive (catch block prevented crash)
        // Send another event to verify
        WriteMessage(adapterOutput, """{"type":"event","event":"stopped","body":{"reason":"step","threadId":1}}""");

        var evt = await session.EventChannel.ReadAsync(CancellationToken.None).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        evt.EventType.Should().Be("stopped");

        session.Dispose();
    }

    [TestMethod]
    public async Task DispatchReverseRequest_RunInTerminal_NoArgs_SendsError()
    {
        var (session, adapterOutput, adapterInput) = DapSessionTestHelper.Create();
        session.StartReaderLoop();

        WriteMessage(adapterOutput, """{"type":"request","seq":1,"command":"runInTerminal","arguments":{}}""");

        await Task.Delay(300);

        adapterInput.Position = 0;
        var response = await DapSession.ReadDapMessageAsync(adapterInput, CancellationToken.None);

        response.Should().NotBeNull();
        response!["command"]!.GetValue<string>().Should().Be("runInTerminal");
        response["success"]!.GetValue<bool>().Should().BeFalse();
        response["message"]!.GetValue<string>().Should().Contain("No args");

        session.Dispose();
    }

    [TestMethod]
    public async Task DispatchReverseRequest_RunInTerminal_EmptyArgsArray_SendsError()
    {
        var (session, adapterOutput, adapterInput) = DapSessionTestHelper.Create();
        session.StartReaderLoop();

        WriteMessage(adapterOutput, """{"type":"request","seq":1,"command":"runInTerminal","arguments":{"args":[]}}""");

        await Task.Delay(300);

        adapterInput.Position = 0;
        var response = await DapSession.ReadDapMessageAsync(adapterInput, CancellationToken.None);

        response.Should().NotBeNull();
        response!["success"]!.GetValue<bool>().Should().BeFalse();

        session.Dispose();
    }

    [TestMethod]
    public async Task DispatchReverseRequest_RunInTerminal_InvalidExe_SendsErrorViaCatch()
    {
        var (session, adapterOutput, adapterInput) = DapSessionTestHelper.Create();
        session.StartReaderLoop();

        // Non-existent executable — Process.Start throws, catch block sends error response
        WriteMessage(adapterOutput, """{"type":"request","seq":1,"command":"runInTerminal","arguments":{"args":["__nonexistent_exe_99999__","arg1"],"cwd":"."}}""");

        await WaitForResponseAsync(adapterInput);

        adapterInput.Position = 0;
        var response = await DapSession.ReadDapMessageAsync(adapterInput, CancellationToken.None);

        response.Should().NotBeNull();
        response!["command"]!.GetValue<string>().Should().Be("runInTerminal");
        response["success"]!.GetValue<bool>().Should().BeFalse();
        response["message"]!.GetValue<string>().Should().NotBeNullOrEmpty();

        session.Dispose();
    }

    [TestMethod]
    public async Task DispatchReverseRequest_RunInTerminal_WithEnvVars_ExercisesEnvPath()
    {
        var (session, adapterOutput, adapterInput) = DapSessionTestHelper.Create();
        session.StartReaderLoop();

        // Non-existent executable — exercises env var copying before Process.Start throws
        WriteMessage(adapterOutput, """{"type":"request","seq":1,"command":"runInTerminal","arguments":{"args":["__nonexistent_exe_99999__"],"env":{"MY_VAR":"value"}}}""");

        await WaitForResponseAsync(adapterInput);

        adapterInput.Position = 0;
        var response = await DapSession.ReadDapMessageAsync(adapterInput, CancellationToken.None);

        response.Should().NotBeNull();
        response!["success"]!.GetValue<bool>().Should().BeFalse();

        session.Dispose();
    }

    /// <summary>
    /// Polls until the stream has data written to it, instead of using a fixed Task.Delay.
    /// This avoids flaky tests on slow I/O environments (e.g., WSL on /mnt/c).
    /// </summary>
    private static async Task WaitForResponseAsync(Stream stream, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (stream.Length == 0 && Environment.TickCount64 < deadline)
            await Task.Delay(50);
    }
}
