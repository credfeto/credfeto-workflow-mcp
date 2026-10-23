using Serilog;

namespace Credfeto.Workflow.Mcp.Server.Helpers;

public static class LoggingConfig
{
    public static LoggerConfiguration CreateConfiguration()
    {
        return new LoggerConfiguration()
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithProcessId()
            .Enrich.WithThreadId()
            .WriteTo.Console();
    }
}
