using EnterpriseCRM.MCP.Services;
using EnterpriseCRM.MCP.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// MCP uses stdout for protocol messages.
// Keep application logs on stderr.
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold =
        LogLevel.Trace;
});

builder.Services.AddSingleton<MockCRMService>();
builder.Services.AddSingleton<CRMTools>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<CRMTools>();

await builder.Build().RunAsync();