using System.Text.Json;
using Xunit;

namespace DDT.Server.Tests;

public sealed class DeploymentOptionsTests(ManualClockApplication application) : IClassFixture<ManualClockApplication>
{
    // The assign dialog predicts with this whether the server finds a machine still at its prompt, so it must be the
    // clock the server decides with, under the name the web UI reads.
    [Fact]
    public async Task TheAssignDialogLearnsTheServersTime()
    {
        SignedInClient administrator = await application.AdministratorAsync();

        using HttpResponseMessage response = await administrator.GetAsync("/api/deployments/options");
        response.EnsureSuccessStatusCode();
        using JsonDocument options = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

        Assert.Equal(application.Clock.GetUtcNow(), options.RootElement.GetProperty("serverUtc").GetDateTimeOffset());
        Assert.False(options.RootElement.GetProperty("domainConfigured").GetBoolean());
        Assert.False(options.RootElement.GetProperty("requireWebApproval").GetBoolean());
        Assert.False(options.RootElement.GetProperty("zeroTouchEnabled").GetBoolean());
    }
}
