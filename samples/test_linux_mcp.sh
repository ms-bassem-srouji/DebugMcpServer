#!/bin/bash
# Test the MCP server on Linux with a .NET dump
DUMP_PATH="$1"
if [ -z "$DUMP_PATH" ]; then
    echo "Usage: $0 <dump-path>"
    exit 1
fi

MCP_DIR="/tmp/debugmcp-linux"
cd "$MCP_DIR"

# MCP protocol: initialize then call tool
INIT='{"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"test"}},"id":0,"jsonrpc":"2.0"}'
CALL="{\"method\":\"tools/call\",\"params\":{\"name\":\"load_dotnet_dump\",\"arguments\":{\"dumpPath\":\"$DUMP_PATH\"}},\"id\":1,\"jsonrpc\":\"2.0\"}"

echo "Testing load_dotnet_dump on Linux..."
echo "Dump: $DUMP_PATH"
echo

# Send both requests, read responses
(echo "$INIT"; sleep 1; echo "$CALL"; sleep 5) | timeout 15 dotnet DebugMcpServer.dll 2>/tmp/mcp_stderr.log | while read -r line; do
    echo "$line" | python3 -m json.tool 2>/dev/null || echo "RAW: $line"
done

echo
echo "=== Stderr (last 5 lines) ==="
tail -5 /tmp/mcp_stderr.log
