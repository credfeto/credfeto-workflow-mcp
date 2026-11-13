using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Workflow.GitHub.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Credfeto.Workflow.Mcp.Server.Helpers;

public static class SelfTestRunner
{
    private const string SELF_TEST_SWITCH = "--self-test";
    private const string GLOBALISATION_CULTURE = "en-GB";
    private const int EXIT_SUCCESS = 0;
    private const int EXIT_FAILURE = 1;

    public static bool IsSelfTest(IReadOnlyList<string> args)
    {
        return args.Any(IsSelfTestSwitch);
    }

    // The switch has no value, so it must not reach the command-line configuration provider, which would
    // otherwise take the argument after it as its value
    public static IReadOnlyList<string> ServerArguments(IReadOnlyList<string> args)
    {
        return [.. args.Where(static arg => !IsSelfTestSwitch(arg))];
    }

    public static async ValueTask<int> RunAsync(
        Func<WebApplication> appFactory,
        TextWriter errorWriter,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await VerifyAsync(appFactory);

            return EXIT_SUCCESS;
        }
        catch (Exception exception)
        {
            string message = $"Self-test failed: {Describe(exception)}";

            await errorWriter.WriteLineAsync(buffer: message.AsMemory(), cancellationToken: cancellationToken);

            return EXIT_FAILURE;
        }
    }

    private static bool IsSelfTestSwitch(string arg)
    {
        return StringComparer.Ordinal.Equals(x: arg, y: SELF_TEST_SWITCH);
    }

    // Never starts the app, so no port is bound, no hosted service runs and no outbound connection is made
    private static async ValueTask VerifyAsync(Func<WebApplication> appFactory)
    {
        await using WebApplication app = appFactory();

        LoadGlobalisation();
        LoadCryptography();

        // Resolving the value runs the options validators that ValidateOnStart would otherwise run on start
        _ = app.Services.GetRequiredService<IOptions<GitHubOptions>>().Value;
        _ = app.Services.GetRequiredService<IOptions<WorkflowOptions>>().Value;
        _ = app.Services.GetRequiredService<IServer>();

        IEndpointRouteBuilder routes = app;
        _ = routes.DataSources.SelectMany(static dataSource => dataSource.Endpoints).Count();
    }

    // A named culture's comparison loads ICU, which is missing from images that only suit invariant globalisation
    private static void LoadGlobalisation()
    {
        _ = CultureInfo
            .GetCultureInfo(GLOBALISATION_CULTURE)
            .CompareInfo.Compare(string1: "self-test", string2: "SELF-TEST", options: CompareOptions.IgnoreCase);
    }

    // Hashing goes through the platform crypto library (OpenSSL on Linux), which TLS to GitHub also needs
    private static void LoadCryptography()
    {
        _ = SHA256.HashData("self-test"u8);
    }

    private static string Describe(Exception exception)
    {
        string chain = string.Join(separator: " ---> ", values: Chain(exception).Select(Summarise));

        return string.Join(separator: ' ', chain.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
    }

    private static string Summarise(Exception exception)
    {
        return $"{exception.GetType().FullName}: {exception.Message}";
    }

    private static IEnumerable<Exception> Chain(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }
}
