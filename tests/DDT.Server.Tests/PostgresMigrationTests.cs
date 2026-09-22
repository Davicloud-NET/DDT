// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Deployments;
using DDT.Contracts.Machines;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Machines;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace DDT.Server.Tests;

// The upgrade to task sequences on a database as image deployments left it. The data part of the migration is
// hand-written SQL, which only this runs.
public sealed class PostgresMigrationTests
{
    private const string ImageDeployments = "20260919150434_ImageLibrary";

    private static DdtDbContext Context(string connectionString)
    {
        ServiceCollection services = new();
        services.AddOptions<IdentityOptions>().Configure(options => options.Stores.SchemaVersion = IdentitySchemaVersions.Version3);

        return new DdtDbContext(new DbContextOptionsBuilder<DdtDbContext>()
            .UseApplicationServiceProvider(services.BuildServiceProvider())
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", DdtDbContext.Schema))
            .Options);
    }

    [Fact]
    public async Task EndsTheImageDeploymentsThatWereActiveAtTheUpgrade()
    {
        PostgreSqlContainer? started = await TestPostgres.StartAsync();
        Assert.SkipWhen(started is null, "Docker is not running, so there is no PostgreSQL to test against. Start Docker to run this test.");

        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using PostgreSqlContainer container = started;
        await using DdtDbContext database = Context(container.GetConnectionString());
        await database.GetService<IMigrator>().MigrateAsync(ImageDeployments, cancellationToken: cancellationToken);

        Guid deploying = Guid.NewGuid();
        Guid waiting = Guid.NewGuid();
        Guid done = Guid.NewGuid();
        Guid running = Guid.NewGuid();
        Guid assigned = Guid.NewGuid();
        Guid older = Guid.NewGuid();
        Guid finished = Guid.NewGuid();
        Guid windows11 = Guid.NewGuid();
        Guid windows10 = Guid.NewGuid();
        DateTimeOffset earlier = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
        DateTimeOffset later = earlier.AddHours(1);

        await database.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO ddt."Machines" ("Id", "SmbiosUuid", "PrimaryMac", "MacAddresses", "State", "TokenGeneration", "FirstSeenUtc", "LastSeenUtc", "ActiveDeploymentId")
            VALUES
                ({deploying}, 'uuid-1', '020000000001', '020000000001', 'Deploying', 3, {earlier}, {later}, {running}),
                ({waiting}, 'uuid-2', '020000000002', '020000000002', 'Pending', 1, {earlier}, {later}, {assigned}),
                ({done}, 'uuid-3', '020000000003', '020000000003', 'Done', 2, {earlier}, {later}, NULL)
            """,
            cancellationToken);

        // Every image deployment named its image, and the library keeps the images.
        await database.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO ddt."Images" ("Id", "Name", "Kind", "Sha256", "SizeBytes", "WimIndex", "InstalledBytes", "UploadedUtc")
            VALUES
                ({windows11}, 'Windows 11 Pro', 'Wim', 'aa', 1, 6, 4, {earlier}),
                ({windows10}, 'Windows 10 Pro', 'Wim', 'bb', 1, 1, 4, {earlier})
            """,
            cancellationToken);

        await database.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO ddt."Deployments" ("Id", "MachineId", "ImageId", "ImageName", "Sha256", "SizeBytes", "WimIndex", "InstalledBytes", "State", "Step", "Percent", "Source", "CreatedUtc", "UpdatedUtc")
            VALUES
                ({running}, {deploying}, {windows11}, 'Windows 11 Pro', 'aa', 1, 6, 4, 'Running', 'Apply', 40, 'Web', {later}, {later}),
                ({assigned}, {waiting}, {windows11}, 'Windows 11 Pro', 'aa', 1, 6, 4, 'Assigned', NULL, 0, 'Web', {later}, {later}),
                ({older}, {done}, {windows10}, 'Windows 10 Pro', 'bb', 1, 1, 4, 'Done', 'Reboot', 100, 'Console', {earlier}, {earlier}),
                ({finished}, {done}, {windows11}, 'Windows 11 Pro', 'aa', 1, 6, 4, 'Done', 'Reboot', 100, 'Web', {later}, {later})
            """,
            cancellationToken);

        await database.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO ddt."MachineLogLines" ("MachineId", "TimestampUtc", "ReceivedUtc", "Level", "Message")
            VALUES ({deploying}, {earlier}, {later}, 'Information', 'Applying the image')
            """,
            cancellationToken);

        await database.Database.MigrateAsync(cancellationToken);
        database.ChangeTracker.Clear();

        Dictionary<Guid, Deployment> deployments = await database.Deployments.AsNoTracking().ToDictionaryAsync(d => d.Id, cancellationToken);
        Dictionary<Guid, Machine> machines = await database.Machines.AsNoTracking().ToDictionaryAsync(m => m.Id, cancellationToken);

        Deployment failed = deployments[running];
        Assert.Equal(DeploymentState.Failed, failed.State);
        Assert.Equal(
            "DDT was upgraded to task sequences while this image was being installed. Assign a task sequence to install the machine again.",
            failed.Error);
        Assert.NotNull(failed.FinishedUtc);
        Assert.Equal("Windows 11 Pro", failed.Title);
        Assert.Equal(0, failed.StepCount);
        Assert.Null(failed.CurrentPhase);
        Assert.Null(failed.TaskSequenceId);

        Deployment cancelled = deployments[assigned];
        Assert.Equal(DeploymentState.Cancelled, cancelled.State);
        Assert.Equal("DDT was upgraded to task sequences before this image was installed. Assign a task sequence instead.", cancelled.Error);

        Assert.Equal(DeploymentState.Done, deployments[finished].State);
        Assert.Null(deployments[finished].Error);
        Assert.Equal("Windows 10 Pro", deployments[older].Title);

        // The image column became the sequence column, and no image id may be taken for a sequence's.
        Assert.All(deployments.Values, deployment => Assert.Null(deployment.TaskSequenceId));
        Assert.Equal(2, await database.Images.CountAsync(i => i.Id == windows11 || i.Id == windows10, cancellationToken));

        Assert.Equal((MachineState.Failed, 4, (Guid?)null, (Guid?)running), Facts(machines[deploying]));
        Assert.Equal((MachineState.Pending, 2, (Guid?)null, (Guid?)assigned), Facts(machines[waiting]));
        Assert.Equal((MachineState.Done, 2, (Guid?)null, (Guid?)finished), Facts(machines[done]));

        // Lines from before kept only the agent's time, uncorrected.
        MachineLogLine line = await database.MachineLogLines.AsNoTracking().SingleAsync(l => l.MachineId == deploying, cancellationToken);
        Assert.Equal(earlier, line.AgentTimestampUtc);
        Assert.Equal(earlier, line.TimestampUtc);
        Assert.Null(line.DeploymentId);
    }

    private static (MachineState, int, Guid?, Guid?) Facts(Machine machine) =>
        (machine.State, machine.TokenGeneration, machine.ActiveDeploymentId, machine.LastDeploymentId);
}
