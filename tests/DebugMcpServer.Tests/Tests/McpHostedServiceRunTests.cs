using System.Text;
using System.Text.Json.Nodes;
using DebugMcpServer.Server;
using DebugMcpServer.Tools;
using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DebugMcpServer.Tests.Tests;

[TestClass]
public class McpHostedServiceRunTests
{
    private static McpHostedService CreateService(IEnumerable<IMcpTool>? tools = null)
    {
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        return new McpHostedService(
            NullLogger<McpHostedService>.Instance,
            lifetime,
            tools ?? []);
    }

    private static (MemoryStream stdin, MemoryStream stdout) CreateStreams(string input)
    {
        var stdin = new MemoryStream(Encoding.UTF8.GetBytes(input));
        var stdout = new MemoryStream();
        return (stdin, stdout);
    }

    private static string GetStdout(MemoryStream stdout)
    {
        stdout.Position = 0;
        return new StreamReader(stdout).ReadToEnd();
    }

    [TestMethod]
    public async Task RunServerAsync_ValidRequest_WritesResponse()
    {
        var svc = CreateService();
        var (stdin, stdout) = CreateStreams("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""" + "\n");

        await svc.RunServerAsync(stdin, stdout, CancellationToken.None);

        var output = GetStdout(stdout);
        output.Should().Contain("\"protocolVersion\"");
        output.Should().Contain("debug-mcp");
    }

    [TestMethod]
    public async Task RunServerAsync_MultipleRequests_ProcessesAll()
    {
        var svc = CreateService();
        var input = string.Join("\n",
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""",
            """{"jsonrpc":"2.0","id":2,"method":"resources/list"}""",
            "");
        var (stdin, stdout) = CreateStreams(input);

        await svc.RunServerAsync(stdin, stdout, CancellationToken.None);

        var output = GetStdout(stdout);
        output.Should().Contain("protocolVersion"); // first response
        output.Should().Contain("resources"); // second response
    }

    [TestMethod]
    public async Task RunServerAsync_BlankLines_AreSkipped()
    {
        var svc = CreateService();
        var input = "\n\n  \n" + """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""" + "\n";
        var (stdin, stdout) = CreateStreams(input);

        await svc.RunServerAsync(stdin, stdout, CancellationToken.None);

        var output = GetStdout(stdout);
        output.Should().Contain("protocolVersion");
    }

    [TestMethod]
    public async Task RunServerAsync_MalformedJson_SkipsAndContinues()
    {
        var svc = CreateService();
        var input = string.Join("\n",
            "not valid json",
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""",
            "");
        var (stdin, stdout) = CreateStreams(input);

        await svc.RunServerAsync(stdin, stdout, CancellationToken.None);

        var output = GetStdout(stdout);
        output.Should().Contain("protocolVersion");
    }

    [TestMethod]
    public async Task RunServerAsync_NullJsonParse_SkipsAndContinues()
    {
        var svc = CreateService();
        // "null" parses to null JsonNode
        var input = string.Join("\n",
            "null",
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""",
            "");
        var (stdin, stdout) = CreateStreams(input);

        await svc.RunServerAsync(stdin, stdout, CancellationToken.None);

        var output = GetStdout(stdout);
        output.Should().Contain("protocolVersion");
    }

    [TestMethod]
    public async Task RunServerAsync_EOF_ExitsGracefully()
    {
        var svc = CreateService();
        var (stdin, stdout) = CreateStreams(""); // empty = immediate EOF

        await svc.RunServerAsync(stdin, stdout, CancellationToken.None);

        // Should not throw, just exit
        GetStdout(stdout).Should().BeEmpty();
    }

    [TestMethod]
    public async Task RunServerAsync_Cancellation_ExitsGracefully()
    {
        var svc = CreateService();
        // Use a stream that blocks forever
        var stdin = new BlockingStream();
        var stdout = new MemoryStream();

        var cts = new CancellationTokenSource(100);

        await svc.RunServerAsync(stdin, stdout, cts.Token);

        // Should not throw
    }

    [TestMethod]
    public async Task ProcessRequestAsync_SuccessfulRequest_WritesToWriter()
    {
        var svc = CreateService();
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""")!;

        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms) { AutoFlush = true };

        await svc.ProcessRequestAsync(request, writer, CancellationToken.None);

        ms.Position = 0;
        var output = new StreamReader(ms).ReadToEnd();
        output.Should().Contain("protocolVersion");
    }

    [TestMethod]
    public async Task ProcessRequestAsync_ToolThrows_WritesErrorResponse()
    {
        var tool = Substitute.For<IMcpTool>();
        tool.Name.Returns("bad_tool");
        tool.ExecuteAsync(Arg.Any<JsonNode?>(), Arg.Any<JsonNode?>(), Arg.Any<CancellationToken>())
            .Returns<JsonNode>(_ => throw new InvalidOperationException("Kaboom"));

        var svc = CreateService([tool]);
        var request = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"bad_tool","arguments":{}}}""")!;

        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms) { AutoFlush = true };

        await svc.ProcessRequestAsync(request, writer, CancellationToken.None);

        ms.Position = 0;
        var output = new StreamReader(ms).ReadToEnd();
        output.Should().Contain("-32603");
        output.Should().Contain("Kaboom");
    }

    [TestMethod]
    public async Task ProcessRequestAsync_ConcurrentRequests_SerializesWrites()
    {
        var svc = CreateService();
        var req1 = JsonNode.Parse("""{"jsonrpc":"2.0","id":1,"method":"resources/list"}""")!;
        var req2 = JsonNode.Parse("""{"jsonrpc":"2.0","id":2,"method":"prompts/list"}""")!;

        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms) { AutoFlush = true };

        // Run concurrently
        await Task.WhenAll(
            svc.ProcessRequestAsync(req1, writer, CancellationToken.None),
            svc.ProcessRequestAsync(req2, writer, CancellationToken.None));

        ms.Position = 0;
        var output = new StreamReader(ms).ReadToEnd();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(2);

        // Each should be valid JSON
        foreach (var line in lines)
        {
            var parsed = JsonNode.Parse(line);
            parsed.Should().NotBeNull();
        }
    }

    [TestMethod]
    public async Task StopAsync_ReturnsCompletedTask_WhenNotStarted()
    {
        var svc = CreateService();

        var task = svc.StopAsync(CancellationToken.None);

        task.IsCompleted.Should().BeTrue();
    }

    /// <summary>A stream that blocks on read until cancelled.</summary>
    private sealed class BlockingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => 0;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
