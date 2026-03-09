# Add Dump File Debugging Support

## Summary

Adds comprehensive crash dump analysis capabilities to DebugMcpServer. Users can now load and analyze .NET and native C/C++ dump files through 18 new MCP tools — no external debugging tools required on Windows.

## What's New

**3 dump debugging engines, zero external dependencies on Windows:**

| Engine | Platform | Dump Type | External Tools |
|--------|----------|-----------|---------------|
| **ClrMD** (Microsoft.Diagnostics.Runtime) | All platforms | .NET dumps | None — MIT library built-in |
| **DbgEng** (dbgeng.dll) | Windows | Native C/C++ dumps | None — ships with Windows |
| **DAP adapters** (netcoredbg, lldb-dap) | Linux/macOS | Core dumps | Adapter must be installed |

## New Tools (18)

### DAP Dump Debugging (3)

- `load_dump_file` — Load dump via DAP adapter (Linux/macOS core dumps)
- `disassemble` — Disassemble machine code at a memory address
- `get_loaded_sources` — List source files the adapter knows about

### .NET Dump Analysis via ClrMD (10)

- `load_dotnet_dump` — Open .NET dump (threads, exceptions, heap, GC)
- `dotnet_dump_threads` — All managed threads with stack traces
- `dotnet_dump_exceptions` — Exceptions with inner chain
- `dotnet_dump_stack_objects` — Objects on a thread's stack
- `dotnet_dump_heap_stats` — Heap statistics by type (filterable)
- `dotnet_dump_find_objects` — Find all instances of a type with addresses
- `dotnet_dump_inspect` — Inspect object fields, arrays, strings
- `dotnet_dump_gc_roots` — Find GC roots keeping an object alive
- `dotnet_dump_memory_stats` — GC generation sizes, segments, committed memory
- `dotnet_dump_async_state` — Async Task/state machine analysis (deadlock diagnosis)

### Native Dump Analysis via DbgEng (2)

- `load_native_dump` — Open Windows .dmp file (uses WinDbg engine)
- `native_dump_command` — Run any WinDbg command (k, ~*k, dv, r, lm, dt, !analyze -v)

### Execution Guards (5 tools updated)

- `continue_execution`, `step_over`, `step_in`, `step_out`, `pause_execution` now return clear errors for dump sessions

## Other Changes

- **`list_adapters` enhanced** — Shows `found`/`not_found`/`bare_command` status, install hints, config file location
- **`list_sessions` updated** — Shows DAP, ClrMD, and DbgEng sessions with type indicator
- **`detach_session` updated** — Handles all 3 session types
- **Bare command name support** — Adapters can be configured as `"netcoredbg"` (resolved from PATH at runtime) instead of full paths
- **`supportsMemoryReferences`** added to DAP initialize for disassembly support
- **`IsDumpSession` flag** on sessions — execution tools blocked with clear error message
- **Security section** added to README — documents that only explicitly configured adapters are executed
- **Default config simplified** — bare command names instead of hardcoded Windows paths

## Samples

- **CrashTarget** (.NET) — Generates .NET dump with random orders. Supports `--seed N` for deterministic data, `--output` for path.
- **NativeCrashTarget** (C++) — Generates native C++ dump with random orders. CMake build, cross-platform (Windows MiniDumpWriteDump, Linux fork+abort). Supports `--seed N` and `--output`.

## Tests

- **504 tests total** (was 257)
- 478 unit tests (cross-platform)
- 10 ClrMD integration tests — generate real .NET dump, run full tool sequence
- 11 DbgEng integration tests — generate real C++ dump, run WinDbg commands
- 5 DbgEng basic tests — COM creation, dump open, WaitForEvent, Execute
- CI updated: Linux filters `TestCategory!=WindowsOnly`

## Architecture

```
Dump file
  ├── .NET (.dmp / core) → ClrMD (Microsoft.Diagnostics.Runtime)
  │     └── 10 structured tools → JSON responses
  ├── Native C++ (.dmp)  → DbgEng (dbgeng.dll, COM interop)
  │     └── WinDbg command interface → text output
  └── Any (via DAP)      → Debug adapter (netcoredbg, lldb-dap)
        └── Existing 31 DAP tools → structured responses
```

## Platform Support

| Platform | .NET Dumps | Native C/C++ Dumps |
|----------|-----------|-------------------|
| **Windows** | ClrMD — fully working (built-in) | DbgEng — fully working (built-in) |
| **Linux** | ClrMD — works with Linux dumps | DAP via lldb-dap/OpenDebugAD7 |
| **macOS** | ClrMD — expected to work | DAP via lldb-dap |

## Stats

- **66 files changed**, 6,226 insertions, 37 deletions
- **46 total MCP tools** (was 31)
- **504 tests** (was 257)
- Windows: 504 pass | Linux: 478 pass, 26 skipped (Windows-only)
