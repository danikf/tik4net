using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using tik4net.mcp;

// The SSH transport ships in the satellite package tik4net.ssh and is not in the built-in registry;
// registering it here is what makes TikConnectionType.Ssh creatable like any other transport.
tik4net.Ssh.Tik4NetSsh.Register();

// Every .tag this server sends says so. The lab router is worked on by the MCP and the integration suite
// at once, and a router log or a wire trace showing a bare counter cannot say which of them a command came
// from — both start counting at 1. Diagnosis only; the router echoes a tag on the session that sent it, so
// the values were never actually confusable.
tik4net.Api.TagSequence.Prefix = "mcp";

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
