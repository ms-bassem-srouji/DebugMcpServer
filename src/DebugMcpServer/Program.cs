using DebugMcpServer.Dap;
using DebugMcpServer.Options;
using DebugMcpServer.Server;
using DebugMcpServer.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DebugMcpServer;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        try
        {
            using IHost host = Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration(builder =>
                {
                    // Bundled defaults (shipped with the tool)
                    builder
                        .SetBasePath(AppContext.BaseDirectory)
                        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);

                    // User-level overrides: ~/.config/debug-mcp-server/appsettings.json
                    var userConfigDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        ".config", "debug-mcp-server");
                    builder.AddJsonFile(
                        Path.Combine(userConfigDir, "appsettings.json"),
                        optional: true, reloadOnChange: false);
                })
                .ConfigureLogging(logging =>
                {
                    // MCP requirement: stdout is for JSON-RPC protocol only, logs go to stderr
                    logging.ClearProviders();
                    logging.SetMinimumLevel(LogLevel.Information);
                    logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
                })
                .ConfigureServices((context, services) =>
                {
                    // Bind debug options
                    services.Configure<DebugOptions>(context.Configuration.GetSection("Debug"));

                    // DAP session registry
                    services.AddSingleton<DapSessionRegistry>();

                    // Register all MCP tools
                    services.AddSingleton<IMcpTool, ListAdaptersTool>();
                    services.AddSingleton<IMcpTool, ListProcessesTool>();
                    services.AddSingleton<IMcpTool, AttachToProcessTool>();
                    services.AddSingleton<IMcpTool, LaunchProcessTool>();
                    services.AddSingleton<IMcpTool, DetachSessionTool>();
                    services.AddSingleton<IMcpTool, TerminateProcessTool>();
                    services.AddSingleton<IMcpTool, SetBreakpointTool>();
                    services.AddSingleton<IMcpTool, RemoveBreakpointTool>();
                    services.AddSingleton<IMcpTool, ListBreakpointsTool>();
                    services.AddSingleton<IMcpTool, SetFunctionBreakpointsTool>();
                    services.AddSingleton<IMcpTool, SetExceptionBreakpointsTool>();
                    services.AddSingleton<IMcpTool, GetExceptionInfoTool>();
                    services.AddSingleton<IMcpTool, SetDataBreakpointTool>();
                    services.AddSingleton<IMcpTool, ListThreadsTool>();
                    services.AddSingleton<IMcpTool, ChangeThreadTool>();
                    services.AddSingleton<IMcpTool, GetCallStackTool>();
                    services.AddSingleton<IMcpTool, GetVariablesTool>();
                    services.AddSingleton<IMcpTool, SetVariableTool>();
                    services.AddSingleton<IMcpTool, ContinueExecutionTool>();
                    services.AddSingleton<IMcpTool, StepOverTool>();
                    services.AddSingleton<IMcpTool, StepInTool>();
                    services.AddSingleton<IMcpTool, StepOutTool>();
                    services.AddSingleton<IMcpTool, PauseExecutionTool>();
                    services.AddSingleton<IMcpTool, GetPendingEventsTool>();
                    services.AddSingleton<IMcpTool, EvaluateExpressionTool>();
                    services.AddSingleton<IMcpTool, ListSessionsTool>();
                    services.AddSingleton<IMcpTool, GetModulesTool>();
                    services.AddSingleton<IMcpTool, GetSourceTool>();
                    services.AddSingleton<IMcpTool, ReadMemoryTool>();
                    services.AddSingleton<IMcpTool, WriteMemoryTool>();
                    services.AddSingleton<IMcpTool, SendDapRequestTool>();

                    // MCP hosted service
                    services.AddHostedService<McpHostedService>();
                })
                .Build();

            Console.Error.WriteLine($"DebugMcpServer v{McpHostedService.ServerVersion}");
            await host.RunAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"MCP Server terminated unexpectedly: {ex}");
        }
    }
}
