using Serilog;

namespace Credfeto.Workflow.Mcp.Server.Helpers;

public static class LoggingConfig
{
    public static LoggerConfiguration CreateConfiguration()
    {
        // Serilog defaults to Information; pass every level through so the Logging:LogLevel configuration decides what is written.
        return new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithProcessId()
            .Enrich.WithThreadId()
            .WriteTo.Console();
    }
}
