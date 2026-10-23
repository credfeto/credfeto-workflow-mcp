using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Credfeto.Workflow.Mcp.Server.Helpers;
using FunFair.Test.Common;
using FunFair.Test.Infrastructure.Mocks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Credfeto.Workflow.Mcp.Server.Tests;

public sealed class KestrelConfigTests : LoggingFolderCleanupTestBase
{
    public KestrelConfigTests(ITestOutputHelper output)
        : base(output) { }

    [Fact]
    public async Task ConfigureKestrelWithoutCertificateAppliesServerOptionsAsync()
    {
        string certPath = Path.Combine(path1: this.TempFolder, path2: "missing.pfx");

        await using WebApplication app = BuildApp(certPath);

        AssertServerOptions(ResolveKestrelOptions(app));
    }

    [Fact]
    public async Task ConfigureKestrelWithCertificateAppliesServerOptionsAsync()
    {
        string certPath = Path.Combine(path1: this.TempFolder, path2: "server.pfx");
        await File.WriteAllBytesAsync(
            path: certPath,
            bytes: CreateSelfSignedPfx(),
            cancellationToken: this.CancellationToken()
        );

        await using WebApplication app = BuildApp(certPath);

        AssertServerOptions(ResolveKestrelOptions(app));
    }

    private static WebApplication BuildApp(string certPath)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Configuration.Sources.Clear();

        _ = builder.WebHost.ConfigureKestrel(certPath: certPath);

        return builder.Build();
    }

    private static KestrelServerOptions ResolveKestrelOptions(WebApplication app)
    {
        return app.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value;
    }

    private static void AssertServerOptions(KestrelServerOptions options)
    {
        Assert.False(condition: options.AddServerHeader, userMessage: "Server header should be disabled");
        Assert.False(condition: options.AllowSynchronousIO, userMessage: "Synchronous IO should be disabled");
        Assert.Null(options.Limits.MinResponseDataRate);
        Assert.Null(options.Limits.MinRequestBodyDataRate);
    }

    private static byte[] CreateSelfSignedPfx()
    {
        using RSA key = RSA.Create(2048);
        CertificateRequest request = new(
            subjectName: "CN=localhost",
            key: key,
            hashAlgorithm: HashAlgorithmName.SHA256,
            padding: RSASignaturePadding.Pkcs1
        );

        using X509Certificate2 certificate = request.CreateSelfSigned(
            notBefore: MockDateTimeSources.Past.GetUtcNow(),
            notAfter: MockDateTimeSources.Future.GetUtcNow()
        );

        return certificate.Export(X509ContentType.Pfx);
    }
}
