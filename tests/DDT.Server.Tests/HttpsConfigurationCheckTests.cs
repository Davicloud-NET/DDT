using DDT.Host.Startup;
using DDT.Server.Configuration;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DDT.Server.Tests;

public sealed class HttpsConfigurationCheckTests
{
    private static readonly IReadOnlySet<DeploymentRole> s_webAndPxe = new HashSet<DeploymentRole> { DeploymentRole.Web, DeploymentRole.Pxe };

    private static void Validate(string httpsUrl, IReadOnlySet<DeploymentRole> roles)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kestrel:Endpoints:Https:Url"] = httpsUrl,
                ["Kestrel:Endpoints:Boot:Url"] = "http://0.0.0.0:8080",
            })
            .Build();

        HttpsConfigurationCheck.Validate(configuration, new DdtOptions(), roles);
    }

    [Theory]
    [InlineData("https://localhost:7152")]
    [InlineData("https://127.0.0.1:7152")]
    [InlineData("https://[::1]:7152")]
    public void RefusesAnEndpointNetbootedMachinesCannotReach(string url)
    {
        InvalidOperationException refusal = Assert.Throws<InvalidOperationException>(() => Validate(url, s_webAndPxe));

        Assert.Contains(url, refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://0.0.0.0:7152")]
    [InlineData("https://*:7152")]
    [InlineData("https://172.26.224.1:7152")]
    [InlineData("https://ddt.example:8443")]
    public void AcceptsAnEndpointOnTheNetwork(string url)
    {
        Validate(url, s_webAndPxe);
    }

    [Fact]
    public void AcceptsLoopbackWithoutThePxeRole()
    {
        Validate("https://localhost:7152", new HashSet<DeploymentRole> { DeploymentRole.Web });
    }
}
