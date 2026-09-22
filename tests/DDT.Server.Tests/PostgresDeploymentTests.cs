// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Images;
using DDT.Contracts.Machines;
using DDT.Contracts.Packages;
using DDT.Contracts.Rules;
using DDT.Contracts.Sequences;
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

    [Fact]
    public async Task StoresTheLibraryOfSequencesPackagesAndRules()
    {
        PostgreSqlContainer? started = await StartAsync();
        Assert.SkipWhen(started is null, "Docker is not running, so there is no PostgreSQL to test against. Start Docker to run this test.");

        await using PostgreSqlContainer container = started;
        using PostgresApplication application = new(container.GetConnectionString());
        SignedInClient administrator = await application.AdministratorAsync();
        Image image = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));

        SequenceView created = await RegisteredMachine.ReadAsync<SequenceView>(await administrator.PostAsync(
            SequenceRequests.Sequences,
            new CreateSequenceRequest("Install", "A NUL\0 in the description", SequenceRequests.Minimal(image.Id))));
        SequenceView saved = await RegisteredMachine.ReadAsync<SequenceView>(await administrator.SaveSequenceAsync(
            created,
            created.Definition with { Steps = [.. created.Definition.Steps, new RebootStep { Id = Guid.NewGuid(), Name = "A NUL\0 in a step" }] },
            "Install Windows"));

        Assert.Equal("A NUL in the description", saved.Description);
        Assert.Equal(2, saved.Revision);
        Assert.Empty(saved.Problems);
        Assert.Equal(HttpStatusCode.BadRequest, (await administrator.CreateSequenceAsync(saved.Definition, "INSTALL WINDOWS")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await administrator.SaveSequenceAsync(created)).StatusCode);

        PackageSummary package = await administrator.UploadedPackageAsync(PackageRequests.DriverZip(), UploadKind.Drivers);
        PackageSummary targeted = await RegisteredMachine.ReadAsync<PackageSummary>(await administrator.PutAsync(
            $"{PackageRequests.Packages}/{package.Id}",
            new UpdatePackageRequest("Latitude", "A NUL\0 here too", [new HardwareModel("Dell Inc.", "Latitude 5440")])));

        Assert.Equal("A NUL here too", targeted.Description);
        Assert.Equal(new HardwareModel("Dell Inc.", "Latitude 5440"), Assert.Single(targeted.Targets));

        await application.AddAssignedRunAsync(ArtifactKind.Drivers, package.Id, package.Sha256);
        Assert.Equal(HttpStatusCode.Conflict, (await administrator.DeleteAsync($"{PackageRequests.Packages}/{package.Id}")).StatusCode);

        AssignmentRuleView rule = await administrator.CreatedRuleAsync(RuleRequests.ModelRule(saved.Id, "Latitude 5440", "Dell Inc."));
        using RegisteredMachine machine = await application.RegisterModelAsync("DELL INC.", "latitude 5440");

        Assert.Equal(rule.Id, (await administrator.ResolutionAsync(machine.Id)).RuleId);
        Assert.Equal(HttpStatusCode.Conflict, (await administrator.PostAsync(RuleRequests.Rules, RuleRequests.ModelRule(saved.Id, "LATITUDE 5440", "dell inc."))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await administrator.DeleteAsync($"{SequenceRequests.Sequences}/{saved.Id}")).StatusCode);
    }
}
