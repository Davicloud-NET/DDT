using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Server.Images;
using DotNet.Testcontainers.Builders;
using Testcontainers.PostgreSql;
using Xunit;

namespace DDT.Server.Tests;

// PostgreSQL refuses what SQLite accepts: a value longer than its column, a NUL, a DateTimeOffset that is not UTC.
public sealed class PostgresDeploymentTests
{
    private static async Task<PostgreSqlContainer?> StartAsync()
    {
        PostgreSqlContainer? container = null;

        try
        {
            container = new PostgreSqlBuilder("postgres:17-alpine").Build();
            await container.StartAsync(TestContext.Current.CancellationToken);

            return container;
        }
        catch (DockerUnavailableException)
        {
            if (container is not null)
            {
                await container.DisposeAsync();
            }

            return null;
        }
    }

    [Fact]
    public async Task AppliesTheMigrationsAndRunsADeployment()
    {
        PostgreSqlContainer? started = await StartAsync();
        Assert.SkipWhen(started is null, "Docker is not running, so there is no PostgreSQL to test against. Start Docker to run this test.");

        await using PostgreSqlContainer container = started;
        using PostgresApplication application = new(container.GetConnectionString());
        SignedInClient administrator = await application.AdministratorAsync();

        // DdtApplication sets an empty connection string, which means SQLite. Without this check, losing the
        // override would pass on SQLite and leave the migrations untested.
        Assert.Equal(
            "Npgsql.EntityFrameworkCore.PostgreSQL",
            await application.QueryAsync(database => Task.FromResult(database.Database.ProviderName)));

        using DeployingMachine machine = await DeployingMachine.ApprovedAsync(
            application,
            administrator,
            [DeployingMachine.Disk(0, "Disk\0 with a NUL and a model name far longer than the sixty four characters its line keeps")]);
        Image image = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));

        MachineSummary assigned = await RegisteredMachine.ReadAsync<MachineSummary>(
            await administrator.PostAsync($"/api/machines/{machine.Id}/deployments", new AssignImageRequest(image.Id, "PC-0006")));
        Guid deployment = assigned.Deployment!.Id;

        await machine.ReportOkAsync(deployment, DeploymentState.Running, DeploymentStep.Partition);
        await machine.ReportOkAsync(deployment, DeploymentState.Running, DeploymentStep.Apply, 50);
        await machine.ReportOkAsync(deployment, DeploymentState.Done, DeploymentStep.Reboot, 100);

        IReadOnlyList<MachineSummary> machines = await RegisteredMachine.ReadAsync<IReadOnlyList<MachineSummary>>(
            await administrator.GetAsync("/api/machines"));
        MachineSummary done = Assert.Single(machines, m => m.Id == machine.Id);

        Assert.Equal(MachineState.Done, done.State);
        Assert.Equal(DeploymentState.Done, done.Deployment?.State);
        Assert.Equal("PC-0006", done.AssignedName);
        Assert.NotNull(done.Deployment?.FinishedUtc);
        Assert.DoesNotContain('\0', done.Disks!);
    }
}
