using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using tik4net.mcp;

// The SSH transport ships in the satellite package tik4net.ssh and is not in the built-in registry;
// registering it here is what makes TikConnectionType.Ssh creatable like any other transport.
tik4net.Ssh.Tik4NetSsh.Register();

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
