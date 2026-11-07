using System.Globalization;

namespace Credfeto.Workflow.GitHub.Tests.Runs;

internal static class RunJson
{
    public const string SHA = "0123456789abcdef0123456789abcdef01234567";

    public static string Run(long id, string status = "completed", string conclusion = "failure", int attempt = 2)
    {
        return "{\"id\":"
            + Number(id)
            + ",\"name\":\"Build\",\"status\":\""
            + status
            + "\",\"conclusion\":\""
            + conclusion
            + "\",\"head_sha\":\""
            + SHA
            + "\",\"head_branch\":\"main\",\"event\":\"push\",\"run_attempt\":"
            + Number(attempt)
            + ",\"created_at\":\"2026-01-02T03:04:05+01:00\"}";
    }

    public static string Job(
        long id,
        long runId,
        string name = "build",
        string status = "completed",
        string conclusion = "failure",
        string steps = ""
    )
    {
        return "{\"id\":"
            + Number(id)
            + ",\"run_id\":"
            + Number(runId)
            + ",\"run_attempt\":2,\"name\":\""
            + name
            + "\",\"status\":\""
            + status
            + "\",\"conclusion\":\""
            + conclusion
            + "\",\"steps\":["
            + steps
            + "]}";
    }

    public static string Step(int number, string name, string conclusion)
    {
        return "{\"name\":\""
            + name
            + "\",\"number\":"
            + Number(number)
            + ",\"status\":\"completed\",\"conclusion\":\""
            + conclusion
            + "\"}";
    }

    public static string Jobs(int total, params string[] jobs)
    {
        return "{\"total_count\":" + Number(total) + ",\"jobs\":[" + string.Join(separator: ',', value: jobs) + "]}";
    }

    private static string Number(long value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
