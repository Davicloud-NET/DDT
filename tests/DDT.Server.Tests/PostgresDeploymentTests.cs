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
using Testcontainers.PostgreSql;
using Xunit;

namespace DDT.Server.Tests;

// PostgreSQL refuses what SQLite accepts: a value longer than its column, a NUL, a DateTimeOffset that is not UTC.
public sealed class PostgresDeploymentTests
{
    [Fact]
    public async Task AppliesTheMigrationsAndRunsADeployment()
    {
        PostgreSqlContainer? started = await TestPostgres.StartAsync();
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
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.Definition(
        [
            .. SequenceRequests.Minimal(image.Id).Steps,
            new RebootStep { Id = Guid.NewGuid(), Name = "A NUL\0 in a step" },
        ]));

        DeploymentSummary run = await administrator.AssignedAsync(machine.Id, sequence.Id, "PC-0006");
        DeploymentView view = await administrator.RunAsync(run.Id);

        Assert.Equal("A NUL in a step", view.Steps[2].Name);
        Assert.Equal(image.Sha256, Assert.Single(view.Artifacts).Sha256);

        IReadOnlyList<MachineSummary> machines = await RegisteredMachine.ReadAsync<IReadOnlyList<MachineSummary>>(
            await administrator.GetAsync("/api/machines"));
        MachineSummary assigned = Assert.Single(machines, m => m.Id == machine.Id);

        Assert.Equal(DeploymentState.Assigned, assigned.Deployment?.State);
        Assert.Equal("PC-0006", assigned.AssignedName);
        Assert.DoesNotContain('\0', assigned.Disks!);
        Assert.Equal(
            [run.Id],
            (await RegisteredMachine.ReadAsync<IReadOnlyList<DeploymentSummary>>(await administrator.GetAsync($"/api/machines/{machine.Id}/deployments"))).Select(r => r.Id));
    }

    [Fact]
    public async Task StoresTheLibraryOfSequencesPackagesAndRules()
    {
        PostgreSqlContainer? started = await TestPostgres.StartAsync();
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
