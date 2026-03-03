using System.Text;
using System.Text.Json.Nodes;
using DebugMcpServer.Dap;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DebugMcpServer.Tests.Tests;

[TestClass]
public class DapSessionRobustnessTests
{
    /// <summary>
    /// Helper: creates a MemoryStream from a raw DAP message (header + body).
    /// </summary>
    private static MemoryStream DapMessage(string jsonBody)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
        var message = $"Content-Length: {bodyBytes.Length}\r\n\r\n{jsonBody}";
        return new MemoryStream(Encoding.UTF8.GetBytes(message));
    }

    private static MemoryStream RawStream(string raw)
        => new(Encoding.UTF8.GetBytes(raw));

    // --- Valid messages ---

    [TestMethod]
    public async Task ReadDapMessage_ValidJson_ReturnsJsonNode()
    {
        using var stream = DapMessage("""{"type":"response","command":"initialize","success":true}""");

        var result = await DapSession.ReadDapMessageAsync(stream, CancellationToken.None);

        result.Should().NotBeNull();
        result!["type"]!.GetValue<string>().Should().Be("response");
        result["command"]!.GetValue<string>().Should().Be("initialize");
    }

    // --- Malformed Content-Length ---

    [TestMethod]
    public async Task ReadDapMessage_NonNumericContentLength_ReturnsNull()
    {
        using var stream = RawStream("Content-Length: abc\r\n\r\n");

        var result = await DapSession.ReadDapMessageAsync(stream, CancellationToken.None);

        result.Should().BeNull();
    }

    [TestMethod]
    public async Task ReadDapMessage_NegativeContentLength_ReturnsNull()
    {
        using var stream = RawStream("Content-Length: -5\r\n\r\n");

        var result = await DapSession.ReadDapMessageAsync(stream, CancellationToken.None);

        // Negative value parsed as int but body read will fail or return null
        result.Should().BeNull();
    }

    [TestMethod]
    public async Task ReadDapMessage_EmptyContentLength_ReturnsNull()
    {
        using var stream = RawStream("Content-Length: \r\n\r\n");

        var result = await DapSession.ReadDapMessageAsync(stream, CancellationToken.None);

        result.Should().BeNull();
    }

    // --- Malformed JSON body ---

    [TestMethod]
    public async Task ReadDapMessage_InvalidJsonBody_ReturnsNull()
    {
        var badJson = "{not valid json at all}}}";
        var bodyBytes = Encoding.UTF8.GetBytes(badJson);
        var raw = $"Content-Length: {bodyBytes.Length}\r\n\r\n{badJson}";
        using var stream = RawStream(raw);

        var result = await DapSession.ReadDapMessageAsync(stream, CancellationToken.None);

        result.Should().BeNull();
    }

    [TestMethod]
    public async Task ReadDapMessage_TruncatedJsonBody_ReturnsNull()
    {
        var truncatedJson = """{"type":"response""";
        var bodyBytes = Encoding.UTF8.GetBytes(truncatedJson);
        var raw = $"Content-Length: {bodyBytes.Length}\r\n\r\n{truncatedJson}";
        using var stream = RawStream(raw);

        var result = await DapSession.ReadDapMessageAsync(stream, CancellationToken.None);

        result.Should().BeNull();
    }

    // --- EOF conditions ---

    [TestMethod]
    public async Task ReadDapMessage_EmptyStream_ReturnsNull()
    {
        using var stream = RawStream("");

        var result = await DapSession.ReadDapMessageAsync(stream, CancellationToken.None);

        result.Should().BeNull();
    }

    [TestMethod]
    public async Task ReadDapMessage_HeaderOnly_NoBody_ReturnsNull()
    {
        // Content-Length says 100 bytes but stream ends after header
        using var stream = RawStream("Content-Length: 100\r\n\r\n");

        var result = await DapSession.ReadDapMessageAsync(stream, CancellationToken.None);

        result.Should().BeNull();
    }

    [TestMethod]
    public async Task ReadDapMessage_ZeroContentLength_ReturnsNull()
    {
        using var stream = RawStream("Content-Length: 0\r\n\r\n");

        var result = await DapSession.ReadDapMessageAsync(stream, CancellationToken.None);

        result.Should().BeNull();
    }

    // --- Multiple messages ---

    [TestMethod]
    public async Task ReadDapMessage_TwoValidMessages_ReadsBothSequentially()
    {
        var json1 = """{"seq":1,"type":"response"}""";
        var json2 = """{"seq":2,"type":"event"}""";
        var body1 = Encoding.UTF8.GetBytes(json1);
        var body2 = Encoding.UTF8.GetBytes(json2);
        var raw = $"Content-Length: {body1.Length}\r\n\r\n{json1}" +
                  $"Content-Length: {body2.Length}\r\n\r\n{json2}";
        using var stream = RawStream(raw);

        var msg1 = await DapSession.ReadDapMessageAsync(stream, CancellationToken.None);
        var msg2 = await DapSession.ReadDapMessageAsync(stream, CancellationToken.None);

        msg1.Should().NotBeNull();
        msg1!["seq"]!.GetValue<int>().Should().Be(1);
        msg2.Should().NotBeNull();
        msg2!["seq"]!.GetValue<int>().Should().Be(2);
    }

    // --- Cancellation ---

    [TestMethod]
    public async Task ReadDapMessage_CancelledToken_ThrowsOperationCancelled()
    {
        using var stream = new MemoryStream();
        // Stream with no data — will block on read, but cancellation should kick in
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => DapSession.ReadDapMessageAsync(stream, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
