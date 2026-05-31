using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using tik4net.mcp;

var builder = Host.CreateApplicationBuilder(args);

// MCP uses stdio transport — log to stderr so stdout stays clean for the protocol
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<MikroTikTools>();

await builder.Build().RunAsync();
