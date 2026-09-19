using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace DDT.Server.Tests;

public sealed class AgentReleaseTests(AgentReleaseApplication application) : IClassFixture<AgentReleaseApplication>
{
    // Literal paths and property names on purpose: agents in boot images built long ago ask exactly these.
    private const string Release = "api/agents/release";
    private const string ReleaseBinary = "api/agents/release/binary";

    [Fact]
    public async Task ServesTheConfiguredAgentAndNoticesWhenItIsReplaced()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using AgentClient agent = new(application.CreateDefaultClient(), TestRemoteAddress.Unique());

        File.Delete(application.BinaryPath);

        Assert.Equal(HttpStatusCode.NotFound, (await agent.GetAsync(Release)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await agent.GetAsync(ReleaseBinary)).StatusCode);

        byte[] first = RandomNumberGenerator.GetBytes(4096);
        await File.WriteAllBytesAsync(application.BinaryPath, first, cancellationToken);
        await AssertReleaseAsync(agent, first);

        HttpResponseMessage binary = await agent.GetAsync(ReleaseBinary);
        Assert.Equal(first, await binary.Content.ReadAsByteArrayAsync(cancellationToken));

        byte[] second = RandomNumberGenerator.GetBytes(5000);
        await File.WriteAllBytesAsync(application.BinaryPath, second, cancellationToken);
        await AssertReleaseAsync(agent, second);

        // A rebuilt agent often has exactly the same size, because the linker pads it.
        byte[] third = RandomNumberGenerator.GetBytes(5000);
        DateTime written = File.GetLastWriteTimeUtc(application.BinaryPath);
        await File.WriteAllBytesAsync(application.BinaryPath, third, cancellationToken);
        File.SetLastWriteTimeUtc(application.BinaryPath, written.AddSeconds(10));
        await AssertReleaseAsync(agent, third);
    }

    private static async Task AssertReleaseAsync(AgentClient agent, byte[] content)
    {
        using JsonDocument release = await ReadJsonAsync(await agent.GetAsync(Release));

        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(content)), release.RootElement.GetProperty("sha256").GetString());
        Assert.Equal(content.Length, release.RootElement.GetProperty("size").GetInt64());
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
