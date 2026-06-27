using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace OpenApiContractValidation.Tests.E2E;

/// <summary>
/// Content-type and streaming-capture E2E tests (scenarios 20-21). Scenario 20 proves the
/// structured-suffix +json wildcard match via the SampleApi; scenario 21 proves that a
/// <c>SendFileAsync</c> response body is captured and committed so the bytes reach the client.
/// </summary>
public class ContentTypeAndMiscE2ETests
{
    [Fact]
    public async Task SuffixWildcardJson_ContentTypeMatchesAndValidates()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/widget");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("+json", response.Content.Headers.ContentType?.MediaType ?? "");
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"sku\"", body);
        Assert.Contains("9.99", body);
    }

    [Fact]
    public async Task SendFileResponse_BytesReachClient()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "e2e-sendfile-" + Guid.NewGuid() + ".bin");
        var expected = Encoding.UTF8.GetBytes("hello-file-bytes");
        await File.WriteAllBytesAsync(tempFile, expected);

        try
        {
            using var host = await E2EHosts.StartRawHostAsync(async ctx =>
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/octet-stream";
                await ctx.Response.SendFileAsync(tempFile, 0, null);
            });
            try
            {
                using var client = host.GetTestServer().CreateClient();
                using var response = await client.GetAsync("/files/report");

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                var bytes = await response.Content.ReadAsByteArrayAsync();
                Assert.Equal(expected, bytes);
            }
            finally
            {
                if (host is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync();
                }
                else
                {
                    host.Dispose();
                }
            }
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
