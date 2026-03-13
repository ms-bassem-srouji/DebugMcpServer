[![CI](https://github.com/ms-bassem-srouji/DebugMcpServer/actions/workflows/ci.yml/badge.svg)](https://github.com/ms-bassem-srouji/DebugMcpServer/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/DebugMcpServer.svg)](https://www.nuget.org/packages/DebugMcpServer)
[![Tests](https://img.shields.io/badge/tests-500%2B%20passed-brightgreen)](https://github.com/ms-bassem-srouji/DebugMcpServer/actions/workflows/ci.yml)
[![Coverage](https://img.shields.io/endpoint?url=https://gist.githubusercontent.com/ms-bassem-srouji/a158c95708696822a98f735f35d18eae/raw/coverage.json)](https://github.com/ms-bassem-srouji/DebugMcpServer/actions/workflows/ci.yml)
[![Platform](https://img.shields.io/badge/platform-windows%20%7C%20linux%20%7C%20macOS-blue)](https://github.com/ms-bassem-srouji/DebugMcpServer)

# Debug MCP Server

A Model Context Protocol (MCP) server that provides interactive debugging capabilities for AI assistants using the [Debug Adapter Protocol (DAP)](https://microsoft.github.io/debug-adapter-protocol/).

Attach to running processes, set breakpoints, step through code, inspect variables, evaluate expressions — all through MCP tool calls. Supports multiple debug adapters (.NET, Python, Node.js, C++), parallel debug sessions, remote debugging over SSH, and comprehensive dump file analysis (.NET via ClrMD, native Windows via DbgEng/WinDbg, cross-platform via DAP adapters).

## Features

- **Multi-adapter support** — Configure multiple debug adapters (netcoredbg, debugpy, js-debug, cpptools) and select which one to use per session
- **Full debugging workflow** — Attach/launch, breakpoints (line, conditional, function, exception, data), stepping, variable inspection, expression evaluation
- **Dump file debugging** — Load crash dumps and core dumps for post-mortem analysis via DAP adapters with stack traces, variable inspection, memory reads, and disassembly
- **.NET dump analysis (ClrMD)** — Built-in .NET dump analysis with no external tools required. Thread enumeration, exception chains, heap statistics, object inspection, GC root analysis, memory stats, and async state machine diagnostics. Cross-platform, MIT-licensed.
- **Native dump analysis (DbgEng)** — Windows-only native `.dmp` analysis using the WinDbg engine (dbgeng.dll). Run any WinDbg command (`!analyze -v`, `k`, `~*k`, `dv`, `lm`, etc.) with zero external tools required.
- **Parallel sessions** — Debug multiple processes simultaneously with concurrent request processing
- **Remote debugging** — Debug processes on remote machines via SSH with zero additional setup
- **Memory access** — Read and write raw memory at arbitrary addresses
- **Source view** — View source code around the current stop location with line numbers
- **Human-readable errors** — Common DAP error codes are translated into actionable guidance

## Platform Compatibility

The MCP server runs on Windows, Linux, and macOS. Most features work across all platforms, but some capabilities are platform-specific:

### Core Debugging

| Feature | Windows | Linux | macOS | Notes |
|---------|:-------:|:-----:|:-----:|-------|
| Launch process | ✅ | ✅ | ✅ | Requires a DAP adapter for the target language |
| Attach to process | ✅ | ✅ | ✅ | |
| Breakpoints (line, conditional, function) | ✅ | ✅ | ✅ | |
| Exception breakpoints | ✅ | ✅ | ✅ | |
| Data breakpoints (watchpoints) | ✅ | ✅ | ✅ | Adapter-dependent |
| Stepping (over, in, out) | ✅ | ✅ | ✅ | |
| Variable inspection | ✅ | ✅ | ✅ | |
| Expression evaluation | ✅ | ✅ | ✅ | |
| Memory read/write | ✅ | ✅ | ✅ | Adapter-dependent |
| Remote debugging (SSH) | ✅ | ✅ | ✅ | |

### Process Discovery

| Feature | Windows | Linux | macOS | Notes |
|---------|:-------:|:-----:|:-----:|-------|
| `list_processes` (name filter) | ✅ | ✅ | ✅ | |
| `list_processes` (moduleFilter) | ✅ | ✅ | ❌ | Uses `Process.Modules` — not supported on macOS. Falls back silently (no crash, just no matches). |
| `list_processes` (remote via SSH) | ✅ | ✅ | ✅ | |

### Dump Analysis

| Feature | Windows | Linux | macOS | Notes |
|---------|:-------:|:-----:|:-----:|-------|
| .NET dump analysis (ClrMD) | ✅ | ✅ | ✅ | Built-in, no external tools needed |
| Native dump analysis (DbgEng) | ✅ | ❌ | ❌ | Uses Windows-only `dbgeng.dll` (WinDbg engine) |
| DAP-based dump loading | ✅ | ✅ | ✅ | Requires appropriate DAP adapter |

### Debug Adapters

| Adapter | Windows | Linux | macOS | Notes |
|---------|:-------:|:-----:|:-----:|-------|
| netcoredbg (.NET) | ✅ | ✅ | ✅ | [Pre-built binaries](https://github.com/Samsung/netcoredbg/releases) available |
| debugpy (Python) | ✅ | ✅ | ✅ | `pip install debugpy` |
| OpenDebugAD7 (C/C++) | ✅ | ✅ | ❌ | VS Code cpptools extension |
| vsdbg (C/C++) | ✅ | ❌ | ❌ | Windows-only |
| lldb-dap (C/C++/Rust) | ❌ | ✅ | ✅ | `apt install lldb` or `brew install llvm` |

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
| `list_processes` | Find running processes by name or by loaded module/DLL (local or remote via SSH) |
| `attach_to_process` | Attach debugger to a running process by PID |
| `launch_process` | Start a process under the debugger |
| `detach_session` | Disconnect debugger, process continues running |
| `terminate_process` | Kill the debugged process and end the session |
| `list_sessions` | List all active debug sessions with state |
| `load_dump_file` | Load a crash dump or core dump for post-mortem debugging |

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
| `disassemble` | Disassemble machine code at a memory address |
| `get_loaded_sources` | List all source files the adapter knows about |

### Memory

| Tool | Description |
|------|-------------|
| `read_memory` | Read raw bytes from a memory address (returns hex dump) |
| `write_memory` | Write raw bytes to a memory address |

### Escape Hatch

| Tool | Description |
|------|-------------|
| `send_dap_request` | Send any arbitrary DAP command directly |

### .NET Dump Analysis (ClrMD)

| Tool | Description |
|------|-------------|
| `load_dotnet_dump` | Load a .NET dump file for analysis (uses ClrMD — no external tools required) |
| `dotnet_dump_threads` | List all managed threads with stack traces |
| `dotnet_dump_exceptions` | Show exceptions on all threads with inner exception chain |
| `dotnet_dump_stack_objects` | Show objects on a thread's stack (locals, params, `this` pointers) |
| `dotnet_dump_heap_stats` | Heap statistics — object counts and sizes by type (filterable) |
| `dotnet_dump_find_objects` | Find all instances of a type on the heap with addresses |
| `dotnet_dump_inspect` | Inspect a .NET object at a given address (fields, arrays, strings) |
| `dotnet_dump_gc_roots` | Find GC roots keeping an object alive (memory leak diagnosis) |
| `dotnet_dump_memory_stats` | GC heap overview: generation sizes, segments, committed memory |
| `dotnet_dump_async_state` | Analyze async Tasks and state machines (deadlock diagnosis) |

### Native Dump Analysis (DbgEng — Windows only)

| Tool | Description |
|------|-------------|
| `load_native_dump` | Load a Windows `.dmp` file for native analysis (uses dbgeng.dll — the WinDbg engine) |
| `native_dump_command` | Run any WinDbg command (k, ~*k, dv, r, lm, u, dd, !analyze -v, etc.) |

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

### Dump File Debugging

> **You:** "Load the crash dump at /tmp/core.12345 using the cpp adapter"
>
> **You:** "Show me the call stack from this dump file"
>
> **You:** "What threads were running when this crash happened?"
>
> **You:** "Disassemble 20 instructions at the crash address"
>
> **You:** "What are the local variables at the crash site?"
>
> **You:** "Show me the loaded source files in this dump"

### .NET Dump Analysis

> **You:** "Open the .NET crash dump at crash_12345.dmp"
>
> **You:** "Show me the managed stack traces for all threads"
>
> **You:** "What exception caused the crash?"
>
> **You:** "What objects were on the crashing thread's stack?"
>
> **You:** "Show me heap statistics — what types are using the most memory?"
>
> **You:** "Find all instances of MyApp.Order on the heap and show me their fields"
>
> **You:** "What's keeping this object alive? Show me the GC roots"
>
> **You:** "How much memory is the GC heap using? Show me generation sizes"
>
> **You:** "Are there any stuck async tasks? Show me the async state"
>
> **You:** "I think we have an async deadlock — show me all tasks that are WaitingForActivation"

### Native Dump Analysis (Windows)

> **You:** "Load the native crash dump at C:\dumps\app.dmp"
>
> **You:** "Run !analyze -v to get the crash analysis"
>
> **You:** "Show me the stack traces for all threads"
>
> **You:** "What are the local variables at the crash frame?"
>
> **You:** "List all loaded modules"
>
> **You:** "Show me the registers"
>
> **You:** "Disassemble the function at the crash address"
>
> **You:** "Display the memory at address 0x7FFE4A3B1000"
>
> **You:** "Show me the type layout for MyStruct"

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

### Dump File Debugging

```
1. load_dump_file(dumpPath: "/tmp/core.12345", program: "/app/myapp", adapter: "cpp")
   → sessionId, process paused at crash site
2. get_callstack()                           → see crash stack trace
3. list_threads()                            → see all threads at crash time
4. get_variables(frameId: 0)                 → inspect locals at crash frame
5. disassemble(memoryReference: "0x4015a0")  → see assembly at crash address
6. get_loaded_sources()                      → discover available source files
7. read_memory(memoryReference: "0x7ffd1000") → examine raw memory
8. evaluate_expression(expression: "strlen(buffer)")
9. detach_session()                          → done
```

Supported adapters and dump formats:

| Adapter | Config Name | Dump Formats | License |
|---------|------------|--------------|---------|
| netcoredbg | `dotnet` | Linux/macOS .NET core dumps | MIT |
| cpptools (OpenDebugAD7) | `cpp` | Linux core dumps | VS Code Extension |
| lldb-dap | `lldb` | Core dumps, Mach-O cores | Open source |
| **ClrMD** (built-in) | *(no config)* | **.NET dumps on any platform** | **MIT** |
| **DbgEng** (built-in) | *(no config)* | **Windows native `.dmp` files** | **Built into Windows** |

#### Platform guidance for dump debugging

| Dump Type | Platform | Approach |
|-----------|----------|----------|
| **.NET dump** | Any | `load_dotnet_dump` — ClrMD built-in, no config needed (threads, exceptions, heap, GC roots) |
| **.NET core dump** | Linux/macOS | `load_dump_file` with `dotnet` adapter — full DAP debugging (variables, expressions) |
| **Native C/C++ core dump** | Linux/macOS | `load_dump_file` with `cpp` or `lldb` adapter — DAP debugging |
| **Native C/C++ `.dmp`** | Windows | `load_native_dump` — DbgEng built-in, full WinDbg command support |

> **Why three approaches?** DAP debugging (via `load_dump_file`) gives structured variable inspection, source mapping, and expression evaluation — but needs a compatible debug adapter. ClrMD (via `load_dotnet_dump`) provides .NET-specific deep analysis (heap stats, GC roots, exception chains) directly as a built-in library — no external tools or configuration needed. DbgEng (via `load_native_dump`) gives full WinDbg command-line access for native Windows dump analysis — also built-in with zero configuration.

Execution control tools (`continue`, `step_*`, `pause`) are automatically blocked on dump sessions with a clear error message.

### .NET Dump Analysis (ClrMD)

For .NET dumps, use the built-in ClrMD integration (MIT-licensed, no external tools required):

```
1. load_dotnet_dump(dumpPath: "crash_12345.dmp")
   → sessionId + runtime info (threads, exceptions, app domains)
2. dotnet_dump_threads()                          → all managed threads with stack traces
3. dotnet_dump_exceptions()                       → exceptions with type, message, inner chain
4. dotnet_dump_stack_objects(osThreadId: "0x9F64") → objects on the crashing thread's stack
5. dotnet_dump_heap_stats(filter: "Order")         → find specific types on the heap
6. dotnet_dump_find_objects(typeName: "Order")     → get addresses of all Order instances
7. dotnet_dump_inspect(address: "0x7fff..")        → object fields, array elements, string values
8. dotnet_dump_gc_roots(address: "0x7fff..")       → what's keeping this object alive?
9. dotnet_dump_memory_stats()                      → GC generation sizes, committed memory
10. dotnet_dump_async_state()                      → async Tasks status, stuck awaits
11. detach_session()                               → close session
```

Powered by [Microsoft.Diagnostics.Runtime (ClrMD)](https://github.com/microsoft/clrmd) — the same library that `dotnet-dump` and Visual Studio use internally. No external tools to install.

### Native Dump Analysis (DbgEng — Windows)

For native Windows `.dmp` files, use the built-in DbgEng integration (uses dbgeng.dll shipped with Windows — no external tools required):

```
1. load_native_dump(dumpPath: "app_crash.dmp")
   → sessionId + thread count, engine version, common commands reference
2. native_dump_command(command: "!analyze -v")    → automated crash analysis
3. native_dump_command(command: "~*k")            → stack traces for all threads
4. native_dump_command(command: "~3s")            → switch to thread 3
5. native_dump_command(command: "dv")             → display local variables
6. native_dump_command(command: "r")              → show registers
7. native_dump_command(command: "lm")             → list loaded modules
8. native_dump_command(command: "u @rip L20")     → disassemble at crash location
9. native_dump_command(command: "dt MyStruct @rbp-0x40")  → display type layout
10. detach_session()                               → close session
```

Powered by dbgeng.dll — the same engine that WinDbg uses. Any WinDbg command works: `k`, `~*k`, `dv`, `r`, `lm`, `u`, `dd`, `db`, `dt`, `!analyze`, `!heap`, `!locks`, etc.

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
│  Tools (46 total)             │  ← Each tool = one MCP capability
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

┌───────────────────────────────┐
│  DotnetDumpRegistry           │  ← .NET dump sessions (ClrMD)
│  DotnetDumpSession            │     In-process, no external tools
├───────────────────────────────┤
│  NativeDumpRegistry           │  ← Native dump sessions (DbgEng)
│  DbgEngSession                │     Windows only, WinDbg engine
└───────────────────────────────┘
```

## Security

The MCP server **only executes debug adapter processes that are explicitly configured** in `appsettings.json` by the user. It does not auto-discover, download, or execute binaries from arbitrary locations.

- **No auto-discovery**: The server will not scan your system to find debug adapters. Every adapter must be explicitly configured by the user.
- **Two path modes supported**:
  - **Full path** (e.g., `/usr/local/bin/netcoredbg`): The server verifies the file exists. `list_adapters` shows `"status": "found"` or `"not_found"`.
  - **Bare command name** (e.g., `netcoredbg`): The user has placed the adapter on their PATH. The server passes it to `Process.Start` which resolves it from PATH. `list_adapters` shows `"status": "bare_command"` since the server cannot verify PATH resolution without executing the binary.
- **No remote code execution**: Debug adapters run as local child processes with the same permissions as the MCP server. SSH remote debugging connects to a user-specified host only.
- **Adapter diagnostics**: Use `list_adapters` to see the status of every configured adapter, install hints for missing ones, and the config file location.
- **User config overrides**: User-level config (`~/.config/debug-mcp-server/appsettings.json`) takes precedence over bundled defaults.

## Configuration

### `appsettings.json`

The default config ships with bare command names — if the adapter is on your PATH, it works out of the box. Override with full paths in your user config for explicit control.

```json
{
  "Debug": {
    "Adapters": [
      {
        "Name": "dotnet",
        "Path": "netcoredbg",
        "AdapterID": "coreclr",
        "RemotePath": "/usr/local/bin/netcoredbg",
        "DumpArgumentName": "coreDumpPath"
      },
      {
        "Name": "cpp",
        "Path": "OpenDebugAD7",
        "AdapterID": "cppdbg",
        "DumpArgumentName": "coreDumpPath"
      }
    ],
    "AttachTimeoutSeconds": 30,
    "StepTimeoutSeconds": 3,
    "ContinueTimeoutSeconds": 25,
    "MaxPendingEvents": 100
  }
}
```

User-level overrides (`~/.config/debug-mcp-server/appsettings.json`) can use full paths:

```json
{
  "Debug": {
    "Adapters": [
      { "Name": "dotnet", "Path": "/usr/local/bin/netcoredbg", "AdapterID": "coreclr", "DumpArgumentName": "coreDumpPath" },
      { "Name": "cppvsdbg", "Path": "C:\\Program Files\\VS\\vsdbg.exe", "AdapterID": "cppvsdbg", "DumpArgumentName": "dumpPath" }
    ]
  }
}
```

| Field | Description |
|-------|-------------|
| `Adapters[].Name` | Friendly name used in tool calls |
| `Adapters[].Path` | Path to the debug adapter executable. Supports full paths (e.g., `/usr/local/bin/netcoredbg`) or bare command names (e.g., `netcoredbg`) if the adapter is on your PATH. |
| `Adapters[].AdapterID` | DAP adapter identifier (e.g., `coreclr`, `python`, `node`) |
| `Adapters[].RemotePath` | Adapter path on remote machines (used with SSH) |
| `Adapters[].DumpArgumentName` | DAP launch argument name for dump file path (e.g., `coreDumpPath`, `coreFile`, `dumpPath`). Required for `load_dump_file` support. |
| `AttachTimeoutSeconds` | Max time to wait for adapter during attach/launch |
| `MaxPendingEvents` | Event channel buffer size per session |

## Testing

```bash
dotnet test
```

The test suite includes 500+ tests: deterministic unit tests (no sleeps, no reflection, no network calls), ClrMD integration tests (generate real .NET dumps), and DbgEng integration tests (generate real native dumps on Windows).

## Project Structure

```
DebugMcpServer/
├── src/DebugMcpServer/
│   ├── Dap/                  # DAP protocol: session, events, SSH helper, error mapping
│   ├── DbgEng/              # Windows native dump analysis (dbgeng.dll/WinDbg engine)
│   ├── DotnetDump/          # .NET dump analysis (ClrMD / Microsoft.Diagnostics.Runtime)
│   ├── Options/              # Configuration models (adapters, timeouts)
│   ├── Server/               # MCP hosted service (stdio transport, concurrent dispatch)
│   └── Tools/                # All 46 MCP tools
├── tests/DebugMcpServer.Tests/
│   ├── Fakes/                # FakeSession, FakeSessionRegistry
│   └── Tests/                # 500+ unit & integration tests
└── samples/
    ├── SampleTarget/          # Sample .NET app for live debugging
    ├── CrashTarget/           # .NET app that generates a self-dump for testing
    └── NativeCrashTarget/     # C++ app that generates a native dump (CMake)
```

## FAQ

### Which adapter do I use for dump files?

| Dump type | Platform | Tool | Config needed? |
|-----------|----------|------|----------------|
| **.NET dump** | Any | `load_dotnet_dump` | No — ClrMD is built-in |
| **Windows `.dmp` (native C/C++)** | Windows | `load_native_dump` | No — DbgEng is built into Windows |
| **Linux/macOS .NET core dump** | Linux/macOS | `load_dump_file` with `dotnet` | Yes — netcoredbg path |
| **Linux core dump (C/C++)** | Linux | `load_dump_file` with `cpp` | Yes — OpenDebugAD7 path |
| **macOS Mach-O core** | macOS | `load_dump_file` with `lldb` | Yes — lldb-dap path |

> **Zero-config on Windows:** Both .NET and native Windows dumps work out of the box — no adapters to install or configure. ClrMD and DbgEng are built-in.

### Where do I find the adapter executables?

| Adapter | Typical location |
|---------|-----------------|
| **OpenDebugAD7 (cpp)** | `~/.vscode/extensions/ms-vscode.cpptools-*/debugAdapters/bin/OpenDebugAD7` |
| **netcoredbg (dotnet)** | Download from [github.com/Samsung/netcoredbg](https://github.com/Samsung/netcoredbg/releases) |
| **debugpy (python)** | `pip install debugpy`, adapter at `python -m debugpy.adapter` |
| **lldb-dap (lldb)** | `apt install lldb` or `brew install llvm` |

### How do I configure an adapter?

Add adapter paths to `~/.config/debug-mcp-server/appsettings.json` (user config, not tracked by git):

```json
{
  "Debug": {
    "Adapters": [
      { "Name": "dotnet", "Path": "/usr/local/bin/netcoredbg", "AdapterID": "coreclr", "DumpArgumentName": "coreDumpPath" },
      { "Name": "cpp", "Path": "/path/to/OpenDebugAD7", "AdapterID": "cppdbg", "DumpArgumentName": "coreDumpPath" }
    ]
  }
}
```

Use `list_adapters` to verify your configuration — it shows which adapters are found, missing, or configured as bare command names.

### Do I need external tools for .NET dump analysis?

**No.** The MCP server includes [ClrMD (Microsoft.Diagnostics.Runtime)](https://github.com/microsoft/clrmd) — the same library that Visual Studio and `dotnet-dump` use internally. Just call `load_dotnet_dump` with a `.dmp` file and use the structured tools (`dotnet_dump_threads`, `dotnet_dump_exceptions`, `dotnet_dump_heap_stats`, etc.). No external tools to install.

## License

MIT
