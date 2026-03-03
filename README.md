[![CI](https://github.com/ms-bassem-srouji/DebugMcpServer/actions/workflows/ci.yml/badge.svg)](https://github.com/ms-bassem-srouji/DebugMcpServer/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/DebugMcpServer.svg)](https://www.nuget.org/packages/DebugMcpServer)

# Debug MCP Server

A Model Context Protocol (MCP) server that provides interactive debugging capabilities for AI assistants using the [Debug Adapter Protocol (DAP)](https://microsoft.github.io/debug-adapter-protocol/).

Attach to running processes, set breakpoints, step through code, inspect variables, evaluate expressions — all through MCP tool calls. Supports multiple debug adapters (.NET, Python, Node.js, C++), parallel debug sessions, and remote debugging over SSH.

## Features

- **Multi-adapter support** — Configure multiple debug adapters (netcoredbg, debugpy, js-debug, cpptools) and select which one to use per session
- **Full debugging workflow** — Attach/launch, breakpoints (line, conditional, function, exception, data), stepping, variable inspection, expression evaluation
- **Parallel sessions** — Debug multiple processes simultaneously with concurrent request processing
- **Remote debugging** — Debug processes on remote machines via SSH with zero additional setup
- **Memory access** — Read and write raw memory at arbitrary addresses
- **Source view** — View source code around the current stop location with line numbers
- **Human-readable errors** — Common DAP error codes are translated into actionable guidance

## Installation

### dotnet tool (recommended)

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later.

```bash
dotnet tool install -g DebugMcpServer
```

This installs the `debug-mcp-server` command globally on your PATH.

### Self-contained binaries

Pre-built binaries with no .NET SDK required are available on [GitHub Releases](https://github.com/ms-bassem-srouji/DebugMcpServer/releases):

| Platform | Asset |
|----------|-------|
| Windows x64 | `debug-mcp-server-win-x64.zip` |
| Linux x64 | `debug-mcp-server-linux-x64.tar.gz` |
| macOS x64 | `debug-mcp-server-osx-x64.tar.gz` |
| macOS ARM64 | `debug-mcp-server-osx-arm64.tar.gz` |

### Build from source

```bash
git clone https://github.com/ms-bassem-srouji/DebugMcpServer.git
cd DebugMcpServer
dotnet build
```

## Quick Start

### Prerequisites

- A DAP-compatible debug adapter:
  - **.NET**: [netcoredbg](https://github.com/Samsung/netcoredbg) (recommended)
  - **Python**: [debugpy](https://github.com/microsoft/debugpy)
  - **Node.js**: [js-debug](https://github.com/microsoft/vscode-js-debug)
  - **C++**: [cpptools](https://github.com/microsoft/vscode-cpptools)

### Configure

Create a config file at `~/.config/debug-mcp-server/appsettings.json` with your adapter paths:

```json
{
  "Debug": {
    "Adapters": [
      { "Name": "dotnet", "Path": "C:\\tools\\netcoredbg\\netcoredbg.exe", "AdapterID": "coreclr", "RemotePath": "/usr/local/bin/netcoredbg" },
      { "Name": "python", "Path": "/usr/bin/debugpy", "AdapterID": "python" }
    ]
  }
}
```

This user-level config overrides the bundled defaults and is preserved across tool updates. If you built from source, you can also edit `src/DebugMcpServer/appsettings.json` directly.

### MCP Client Configuration

### Install for Your AI Client

#### Claude Code (CLI & VS Code Extension)

Add to your project's `.mcp.json` file (or `~/.claude/settings.json` for global):

```json
{
  "mcpServers": {
    "debugger": {
      "command": "debug-mcp-server"
    }
  }
}
```

Or via the CLI:

```bash
claude mcp add debugger -- debug-mcp-server
```

<details>
<summary>Alternative: Build from source</summary>

```json
{
  "mcpServers": {
    "debugger": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/DebugMcpServer/src/DebugMcpServer"]
    }
  }
}
```

CLI: `claude mcp add debugger -- dotnet run --project /path/to/DebugMcpServer/src/DebugMcpServer`

</details>

#### VS Code (GitHub Copilot)

Add to your workspace `.vscode/mcp.json`:

```json
{
  "servers": {
    "debugger": {
      "type": "stdio",
      "command": "debug-mcp-server"
    }
  }
}
```

Or add to your user settings (`settings.json`):

```json
{
  "mcp": {
    "servers": {
      "debugger": {
        "type": "stdio",
        "command": "debug-mcp-server"
      }
    }
  }
}
```

<details>
<summary>Alternative: Build from source</summary>

```json
{
  "servers": {
    "debugger": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/DebugMcpServer/src/DebugMcpServer"]
    }
  }
}
```

</details>

#### GitHub Copilot CLI

Add to `~/.copilot/mcp-config.json`:

```json
{
  "mcpServers": {
    "debugger": {
      "type": "stdio",
      "command": "debug-mcp-server"
    }
  }
}
```

<details>
<summary>Alternative: Build from source</summary>

```json
{
  "mcpServers": {
    "debugger": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/DebugMcpServer/src/DebugMcpServer"]
    }
  }
}
```

</details>

#### Cursor

Add to your project's `.cursor/mcp.json`:

```json
{
  "mcpServers": {
    "debugger": {
      "command": "debug-mcp-server"
    }
  }
}
```

<details>
<summary>Alternative: Build from source</summary>

```json
{
  "mcpServers": {
    "debugger": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/DebugMcpServer/src/DebugMcpServer"]
    }
  }
}
```

</details>

#### Windsurf

Add to `~/.codeium/windsurf/mcp_config.json`:

```json
{
  "mcpServers": {
    "debugger": {
      "command": "debug-mcp-server"
    }
  }
}
```

<details>
<summary>Alternative: Build from source</summary>

```json
{
  "mcpServers": {
    "debugger": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/DebugMcpServer/src/DebugMcpServer"]
    }
  }
}
```

</details>

> **Note**: The `debug-mcp-server` command is available on your PATH after running `dotnet tool install -g DebugMcpServer`. If you are using the build-from-source configs, replace `/path/to/DebugMcpServer` with the actual path where you cloned the repository. On Windows, use backslashes or forward slashes (e.g., `C:/repos/DebugMcpServer/src/DebugMcpServer`).

## Tools

### Session Management

| Tool | Description |
|------|-------------|
| `list_adapters` | List configured debug adapters |
| `list_processes` | Find running processes by name (local or remote via SSH) |
| `attach_to_process` | Attach debugger to a running process by PID |
| `launch_process` | Start a process under the debugger |
| `detach_session` | Disconnect debugger, process continues running |
| `terminate_process` | Kill the debugged process and end the session |
| `list_sessions` | List all active debug sessions with state |

### Breakpoints

| Tool | Description |
|------|-------------|
| `set_breakpoint` | Set breakpoint by file + line, with optional `condition` and `hitCount` |
| `remove_breakpoint` | Remove breakpoint by file + line |
| `list_breakpoints` | List all active breakpoints in a session |
| `set_function_breakpoints` | Break on function entry by name |
| `set_exception_breakpoints` | Break on thrown/unhandled exceptions |
| `get_exception_info` | Get exception type, message, and stack trace when stopped on one |
| `set_data_breakpoint` | Break when a variable's value changes (watchpoint) |

### Execution Control

| Tool | Description |
|------|-------------|
| `continue_execution` | Resume execution (configurable wait timeout, default 3s) |
| `step_over` | Step over current line |
| `step_in` | Step into function call |
| `step_out` | Step out of current function |
| `pause_execution` | Pause a running process |
| `get_pending_events` | Drain queued DAP events (output, stopped, thread) |

### Inspection

| Tool | Description |
|------|-------------|
| `get_source` | View source code around current stop location |
| `get_callstack` | Get stack frames for active thread |
| `get_variables` | Inspect variables in a stack frame or expand nested objects |
| `set_variable` | Modify a variable's value while paused |
| `evaluate_expression` | Evaluate any expression in the current frame context |
| `list_threads` | List all threads |
| `change_thread` | Switch active thread |
| `get_modules` | List loaded modules/assemblies |

### Memory

| Tool | Description |
|------|-------------|
| `read_memory` | Read raw bytes from a memory address (returns hex dump) |
| `write_memory` | Write raw bytes to a memory address |

### Escape Hatch

| Tool | Description |
|------|-------------|
| `send_dap_request` | Send any arbitrary DAP command directly |

## Sample Prompts

These are natural language prompts you can give to your AI assistant. The assistant will translate them into the appropriate MCP tool calls automatically.

### Getting Started

> **You:** "What debug adapters do you have?"
>
> **You:** "Find the MyWebApp process"
>
> **You:** "Attach the debugger to process 1234 using the dotnet adapter"
>
> **You:** "Launch `bin/Debug/net8.0/MyApp.exe` under the debugger"

### Setting Breakpoints

> **You:** "Set a breakpoint at line 42 in Program.cs"
>
> **You:** "Add a conditional breakpoint on line 85 of OrderService.cs when orderId equals 100"
>
> **You:** "Break when we enter the ProcessPayment function"
>
> **You:** "Break on any unhandled exceptions"
>
> **You:** "Set a watchpoint on the `_balance` variable — break when it changes"
>
> **You:** "Show me all active breakpoints"

### Execution Control

> **You:** "Resume execution"
>
> **You:** "Resume and wait 20 seconds for the next breakpoint"
>
> **You:** "Step over the current line"
>
> **You:** "Step into this function call"
>
> **You:** "Step out of the current function"
>
> **You:** "Pause the process"

### Inspecting State

> **You:** "Show me the source code around where we stopped"
>
> **You:** "What's the call stack?"
>
> **You:** "Show me the local variables"
>
> **You:** "What is the value of `customer.Name`?"
>
> **You:** "Evaluate `orders.Where(o => o.Status == "Pending").Count()`"
>
> **You:** "Expand the `orderItems` variable — show me its properties"
>
> **You:** "What threads are running? Switch to thread 5"
>
> **You:** "What modules are loaded?"

### Modifying State

> **You:** "Set the `retryCount` variable to 0"
>
> **You:** "Write 0xFF to memory address 0x7FFE4A3B1000"

### Breakpoint Management

> **You:** "Remove the breakpoint at line 42 in Program.cs"
>
> **You:** "Clear all breakpoints and resume"

### Exception Handling

> **You:** "Break on all exceptions"
>
> **You:** "What exception just occurred? Show me the details and stack trace"
>
> **You:** "Break only on unhandled exceptions"

### Session Management

> **You:** "Is the process still running or paused?"
>
> **You:** "List all debug sessions"
>
> **You:** "Stop debugging and detach"
>
> **You:** "Kill the process and end the debug session"

### Remote Debugging

> **You:** "List processes on the remote server user@192.168.1.50"
>
> **You:** "Attach to process 5678 on user@production-server using the dotnet adapter"
>
> **You:** "Debug the MyApp process on the staging server via SSH"

### Advanced Workflows

> **You:** "Attach to the MyApp process, set a breakpoint when `orderTotal > 1000` on line 55 of CheckoutService.cs, then resume and wait for it to hit"
>
> **You:** "I'm debugging a race condition — attach to the process, break on all exceptions, and when it stops show me the call stack and all local variables"
>
> **You:** "Step through the next 5 lines and show me how the `total` variable changes at each step"
>
> **You:** "Debug two processes side by side — attach to both PIDs 1234 and 5678, set the same breakpoint in both, and resume both"

## Usage Examples (Tool Calls)

For reference, here's how the prompts above map to actual tool calls:

### Basic Debugging

```
1. list_processes(filter: "MyApp")           → find PID
2. attach_to_process(pid: 1234)              → get sessionId, process paused
3. set_breakpoint(file: "Program.cs", line: 42, condition: "x > 10")
4. continue_execution()                      → hits breakpoint
5. get_source()                              → see code context
6. get_callstack()                           → get frame IDs
7. get_variables(frameId: 1)                 → inspect locals
8. evaluate_expression(expression: "myList.Count")
9. step_over()                               → next line
10. detach_session()                          → done
```

### Remote Debugging over SSH

```
1. list_processes(host: "user@server", filter: "myapp")
2. attach_to_process(pid: 5678, host: "user@server", adapter: "dotnet")
3. ... all tools work the same — DAP flows through SSH
```

Requires SSH key-based authentication (password auth is not supported since the MCP server runs non-interactively).

### Parallel Sessions

```
1. attach_to_process(pid: 1234)  → sessionId: "aaa"
2. attach_to_process(pid: 5678)  → sessionId: "bbb"
3. continue_execution(sessionId: "aaa")           → runs in parallel
4. get_callstack(sessionId: "bbb")                → works immediately
5. list_sessions()                                 → see both sessions
```

## Architecture

```
MCP Client (Claude, etc.)
    │
    │ JSON-RPC over stdio
    │
┌───▼───────────────────────────┐
│  McpHostedService             │  ← Concurrent request dispatch
│  (MCP protocol handler)       │     with stdout write lock
├───────────────────────────────┤
│  Tools (31 total)             │  ← Each tool = one MCP capability
│  AttachToProcessTool          │
│  SetBreakpointTool            │
│  GetVariablesTool  ...        │
├───────────────────────────────┤
│  DapSessionRegistry           │  ← Thread-safe session store
│  (ConcurrentDictionary)       │     (multiple parallel sessions)
├───────────────────────────────┤
│  DapSession                   │  ← DAP protocol over stdin/stdout
│  (per debug adapter process)  │     or piped through SSH
├───────────────────────────────┤
│  Debug Adapter (netcoredbg)   │  ← Local process or remote via SSH
│  Debug Adapter (debugpy)      │
└───────────────────────────────┘
```

## Configuration

### `appsettings.json`

```json
{
  "Debug": {
    "Adapters": [
      {
        "Name": "dotnet",
        "Path": "C:\\tools\\netcoredbg\\netcoredbg.exe",
        "AdapterID": "coreclr",
        "RemotePath": "/usr/local/bin/netcoredbg"
      }
    ],
    "AttachTimeoutSeconds": 30,
    "StepTimeoutSeconds": 3,
    "ContinueTimeoutSeconds": 25,
    "MaxPendingEvents": 100
  }
}
```

| Field | Description |
|-------|-------------|
| `Adapters[].Name` | Friendly name used in tool calls |
| `Adapters[].Path` | Local path to the debug adapter executable |
| `Adapters[].AdapterID` | DAP adapter identifier (e.g., `coreclr`, `python`, `node`) |
| `Adapters[].RemotePath` | Adapter path on remote machines (used with SSH) |
| `AttachTimeoutSeconds` | Max time to wait for adapter during attach/launch |
| `MaxPendingEvents` | Event channel buffer size per session |

## Testing

```bash
dotnet test
```

The test suite includes 257+ deterministic unit tests with no external dependencies (no sleeps, no reflection, no network calls).

## Project Structure

```
DebugMcpServer/
├── src/DebugMcpServer/
│   ├── Dap/                  # DAP protocol: session, events, SSH helper, error mapping
│   ├── Options/              # Configuration models (adapters, timeouts)
│   ├── Server/               # MCP hosted service (stdio transport, concurrent dispatch)
│   └── Tools/                # All 29 MCP tools
├── tests/DebugMcpServer.Tests/
│   ├── Fakes/                # FakeSession, FakeSessionRegistry
│   └── Tests/                # Unit tests for every tool
└── samples/SampleTarget/     # Sample .NET app for testing
```

## License

TBD — License to be determined. Please check with the repository owner before using in production.
