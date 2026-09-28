// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text.Json;
using DDT.Contracts;
using DDT.Contracts.Deployments;
using DDT.Contracts.Images;
using DDT.Contracts.Machines;
using DDT.Contracts.Rules;
using DDT.Contracts.Sequences;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Images;
using DDT.Server.Machines;
using DDT.Server.Rules;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;
using DeploymentStep = DDT.Server.Deployments.DeploymentStep;

namespace DDT.Server.Tests;

// Upgrades of databases as earlier builds left them. The data parts of migrations are hand-written SQL, which only these
// tests run.
public sealed class PostgresMigrationTests
{
    private const string ImageDeployments = "20260919150434_ImageLibrary";
    private const string TaskSequences = "20260922110344_TaskSequences";
    private const string SettingsStore = "20260927015307_SettingsStore";

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

    // Raw disk images only add columns: what a database held before keeps its values, and the new ones say nothing.
    [Fact]
    public async Task KeepsWhatATaskSequenceDatabaseHeldWhenItAddsRawDiskImages()
    {
        PostgreSqlContainer? started = await TestPostgres.StartAsync();
        Assert.SkipWhen(started is null, "Docker is not running, so there is no PostgreSQL to test against. Start Docker to run this test.");

        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using PostgreSqlContainer container = started;
        await using DdtDbContext database = Context(container.GetConnectionString());
        await database.GetService<IMigrator>().MigrateAsync(TaskSequences, cancellationToken: cancellationToken);

        Guid machine = Guid.NewGuid();
        Guid image = Guid.NewGuid();
        Guid run = Guid.NewGuid();
        Guid step = Guid.NewGuid();
        DateTimeOffset now = new(2026, 9, 24, 8, 0, 0, TimeSpan.Zero);

        await database.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO ddt."Machines" ("Id", "SmbiosUuid", "PrimaryMac", "MacAddresses", "State", "TokenGeneration", "SequenceVersion", "FirstSeenUtc", "LastSeenUtc")
            VALUES ({machine}, 'uuid-1', '020000000001', '020000000001', 'Done', 1, 1, {now}, {now})
            """,
            cancellationToken);
        await database.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO ddt."Images" ("Id", "Name", "Kind", "Sha256", "SizeBytes", "WimIndex", "InstalledBytes", "UploadedUtc")
            VALUES ({image}, 'Windows 11 Pro', 'Wim', 'aa', 1, 6, 4, {now})
            """,
            cancellationToken);
        await database.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO ddt."Deployments" ("Id", "MachineId", "Title", "State", "Source", "Percent", "StepCount", "CreatedUtc", "UpdatedUtc")
            VALUES ({run}, {machine}, 'Install Windows', 'Done', 'Web', 100, 1, {now}, {now})
            """,
            cancellationToken);
        await database.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO ddt."DeploymentArtifacts" ("DeploymentId", "StepId", "Kind", "SourceId", "Name", "Sha256", "SizeBytes", "ExpandedBytes", "WimIndex")
            VALUES ({run}, {step}, 'Image', {image}, 'Windows 11 Pro', 'aa', 1, 4, 6)
            """,
            cancellationToken);

        await database.Database.MigrateAsync(cancellationToken);
        database.ChangeTracker.Clear();

        Assert.Null((await database.Machines.AsNoTracking().SingleAsync(m => m.Id == machine, cancellationToken)).SecureBootEnabled);
        Image windows = await database.Images.AsNoTracking().SingleAsync(i => i.Id == image, cancellationToken);
        Assert.Equal(
            (ImageKind.Wim, "Windows 11 Pro", (ImageBootCapability?)null, (string?)null, (string?)null),
            (windows.Kind, windows.Name, windows.BootCapability, windows.BootDetail, windows.SourceSha256));
        Deployment deployment = await database.Deployments.AsNoTracking().SingleAsync(d => d.Id == run, cancellationToken);
        Assert.Equal((DeploymentState.Done, false), (deployment.State, deployment.AllowSecureBootMismatch));
        DeploymentArtifact artifact = await database.DeploymentArtifacts.AsNoTracking().SingleAsync(a => a.DeploymentId == run, cancellationToken);
        Assert.Equal((image, (ImageBootCapability?)null), (artifact.SourceId, artifact.BootCapability));

        // A raw disk image is found by the SHA-256 of its disk, as a second upload of that disk is.
        database.Images.Add(new Image
        {
            Id = Guid.NewGuid(),
            Name = "noble-server-cloudimg-amd64",
            Kind = ImageKind.RawDisk,
            Sha256 = "bb",
            SizeBytes = 1,
            InstalledBytes = 4,
            UploadedUtc = now,
            BootCapability = ImageBootCapability.NotSigned,
            BootDetail = @"\EFI\BOOT\BOOTX64.EFI carries no signature.",
            SourceSha256 = "cc",
        });
        await database.SaveChangesAsync(cancellationToken);
        database.ChangeTracker.Clear();

        Image raw = await database.Images.AsNoTracking().SingleAsync(i => i.SourceSha256 == "cc", cancellationToken);
        Assert.Equal((ImageKind.RawDisk, ImageBootCapability.NotSigned), (raw.Kind, raw.BootCapability));
    }

    // The assignment rules become the top of the ordered list in the order the server tried them, each with a condition
    // that matches the machines it matched, and a run from before trees reads as it did.
    [Fact]
    public async Task CopiesTheAssignmentRulesToTheTopOfTheRulesInTheOrderTheyWereTried()
    {
        PostgreSqlContainer? started = await TestPostgres.StartAsync();
        Assert.SkipWhen(started is null, "Docker is not running, so there is no PostgreSQL to test against. Start Docker to run this test.");

        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        await using PostgreSqlContainer container = started;
        await using DdtDbContext database = Context(container.GetConnectionString());
        await database.GetService<IMigrator>().MigrateAsync(SettingsStore, cancellationToken: cancellationToken);

        Guid windows = Guid.NewGuid();
        Guid ubuntu = Guid.NewGuid();
        string definition = """{"version":1,"steps":[]}""";
        DateTimeOffset created = new(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);
        DateTimeOffset updated = created.AddDays(1);

        // Each rule's id is lower than those of the rules it has to come after, so only the ordering can put it there. The
        // OptiPlex rules tie on everything but their ids, which differ in the first byte only: a signed comparison would
        // put 80... first, and .NET and PostgreSQL both put 10... first.
        Guid shortPrefix = Guid.Parse("00000000-0000-0000-0000-000000000001");
        Guid anyPrefix = Guid.Parse("00000000-0000-0000-0000-000000000002");
        Guid dellPrefix = Guid.Parse("00000000-0000-0000-0000-000000000003");
        Guid quoted = Guid.Parse("00000000-0000-0000-0000-000000000004");
        Guid anyExact = Guid.Parse("00000000-0000-0000-0000-000000000005");
        Guid dellExact = Guid.Parse("00000000-0000-0000-0000-000000000006");
        Guid macLow = Guid.Parse("00000000-0000-0000-0000-00000000000e");
        Guid macHigh = Guid.Parse("00000000-0000-0000-0000-00000000000f");
        Guid tieLow = Guid.Parse("10000000-0000-0000-0000-000000000000");
        Guid tieHigh = Guid.Parse("80000000-0000-0000-0000-000000000000");
        Assert.True(tieLow.CompareTo(tieHigh) < 0);

        // What JSON and SQL have to escape.
        string quotedMaker = "ACME \"Quote\" \\ Co";
        string quotedModel = "X \"1\"";
        string quotedKey = "model:ACME \"QUOTE\" \\ CO|X \"1\"";

        await database.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO ddt."TaskSequences" ("Id", "Name", "NormalizedName", "Definition", "Revision", "CreatedUtc", "UpdatedUtc")
            VALUES
                ({windows}, 'Install Windows', 'INSTALL WINDOWS', {definition}, 1, {created}, {created}),
                ({ubuntu}, 'Install Ubuntu', 'INSTALL UBUNTU', {definition}, 1, {created}, {created})
            """,
            cancellationToken);
        await database.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO ddt."AssignmentRules" ("Id", "Kind", "MatchKey", "Mac", "Manufacturer", "Model", "TaskSequenceId", "Description", "CreatedUtc", "UpdatedUtc", "UpdatedByName")
            VALUES
                ({shortPrefix}, 'Model', 'model:|LAT*', NULL, NULL, 'Lat*', {windows}, NULL, {created}, {updated}, 'alice'),
                ({anyPrefix}, 'Model', 'model:|LATITUDE 7*', NULL, NULL, 'Latitude 7*', {windows}, NULL, {created}, {updated}, 'alice'),
                ({dellPrefix}, 'Model', 'model:DELL INC.|LATITUDE 7*', NULL, 'Dell Inc.', 'Latitude 7*', {ubuntu}, NULL, {created}, {updated}, 'alice'),
                ({quoted}, 'Model', {quotedKey}, NULL, {quotedMaker}, {quotedModel}, {windows}, NULL, {created}, {updated}, 'alice'),
                ({anyExact}, 'Model', 'model:|LATITUDE 5440', NULL, NULL, 'Latitude 5440', {windows}, NULL, {created}, {updated}, 'alice'),
                ({dellExact}, 'Model', 'model:DELL INC.|LATITUDE 5440', NULL, 'Dell Inc.', 'Latitude 5440', {ubuntu}, 'Finance laptops', {created}, {updated}, 'alice'),
                ({tieHigh}, 'Model', 'model:|OPTIPLEX 7010', NULL, NULL, 'OptiPlex 7010', {windows}, NULL, {created}, {updated}, NULL),
                ({tieLow}, 'Model', 'model:|OPTIPLEX 7020', NULL, NULL, 'OptiPlex 7020', {windows}, NULL, {created}, {updated}, NULL),
                ({macHigh}, 'Mac', 'mac:00155D0102FF', '00155D0102FF', NULL, NULL, {ubuntu}, NULL, {created}, {updated}, 'alice'),
                ({macLow}, 'Mac', 'mac:00155D010203', '00155D010203', NULL, NULL, {windows}, 'The lab PC', {created}, {updated}, 'alice')
            """,
            cancellationToken);

        // A run from before trees, which a rule chose.
        Guid machine = Guid.NewGuid();
        Guid run = Guid.NewGuid();
        Guid step = Guid.NewGuid();
        await database.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO ddt."Machines" ("Id", "SmbiosUuid", "PrimaryMac", "MacAddresses", "State", "TokenGeneration", "SequenceVersion", "FirstSeenUtc", "LastSeenUtc")
            VALUES ({machine}, 'uuid-1', '00155D010203', '00155D010203', 'Done', 1, 2, {created}, {created})
            """,
            cancellationToken);
        await database.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO ddt."Deployments" ("Id", "MachineId", "TaskSequenceId", "RuleId", "Title", "State", "Source", "Percent", "StepCount", "CreatedUtc", "UpdatedUtc")
            VALUES ({run}, {machine}, {windows}, {macLow}, 'Install Windows', 'Done', 'Rule', 100, 1, {created}, {created})
            """,
            cancellationToken);
        await database.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO ddt."DeploymentSteps" ("DeploymentId", "StepId", "Index", "Name", "Kind", "Phase", "State", "Percent")
            VALUES ({run}, {step}, 0, 'Partition', 'partition', 'WindowsPE', 'Done', 100)
            """,
            cancellationToken);

        await database.Database.MigrateAsync(cancellationToken);
        database.ChangeTracker.Clear();

        List<Rule> rules = await database.Rules.AsNoTracking().OrderBy(r => r.Position).ToListAsync(cancellationToken);

        Assert.Equal([macLow, macHigh, dellExact, anyExact, tieLow, tieHigh, quoted, dellPrefix, anyPrefix, shortPrefix], rules.Select(r => r.Id));
        Assert.Equal(Enumerable.Range(0, 10), rules.Select(r => r.Position));
        Assert.Equal(
            [
                "MAC address 00:15:5D:01:02:03",
                "MAC address 00:15:5D:01:02:FF",
                "Model Dell Inc. Latitude 5440",
                "Model Latitude 5440 of any maker",
                "Model OptiPlex 7020 of any maker",
                "Model OptiPlex 7010 of any maker",
                $"Model {quotedMaker} {quotedModel}",
                "Model Dell Inc. Latitude 7*",
                "Model Latitude 7* of any maker",
                "Model Lat* of any maker",
            ],
            rules.Select(r => r.Name));

        // Read through the contracts, as the rules page reads a rule.
        Dictionary<Guid, RuleView> views = rules.ToDictionary(
            r => r.Id,
            r => JsonSerializer.Deserialize(JsonSerializer.Serialize(View(r), DdtJsonContext.Default.RuleView), DdtJsonContext.Default.RuleView)!);

        Assert.Equal(Json(Test(MachineVariableNames.MacAddress, ConditionOperator.Equals, "00:15:5D:01:02:03")), Json(views[macLow].When));
        Assert.Equal(Json(Test(MachineVariableNames.MacAddress, ConditionOperator.Equals, "00:15:5D:01:02:FF")), Json(views[macHigh].When));
        Assert.Equal(
            Json(All(
                Test(MachineVariableNames.Manufacturer, ConditionOperator.Equals, "Dell Inc."),
                Test(MachineVariableNames.Model, ConditionOperator.Equals, "Latitude 5440"))),
            Json(views[dellExact].When));
        Assert.Equal(Json(All(Test(MachineVariableNames.Model, ConditionOperator.Equals, "Latitude 5440"))), Json(views[anyExact].When));
        Assert.Equal(
            Json(All(
                Test(MachineVariableNames.Manufacturer, ConditionOperator.Equals, quotedMaker),
                Test(MachineVariableNames.Model, ConditionOperator.Equals, quotedModel))),
            Json(views[quoted].When));
        Assert.Equal(
            Json(All(
                Test(MachineVariableNames.Manufacturer, ConditionOperator.Equals, "Dell Inc."),
                Test(MachineVariableNames.Model, ConditionOperator.Matches, "Latitude 7*"))),
            Json(views[dellPrefix].When));
        Assert.Equal(Json(All(Test(MachineVariableNames.Model, ConditionOperator.Matches, "Lat*"))), Json(views[shortPrefix].When));

        Assert.All(views.Values, view =>
        {
            Assert.True(view.Enabled);
            Assert.Empty(view.Values);
            Assert.Empty(view.RoleIds);
            Assert.Equal(1, view.Revision);
            Assert.Equal(updated, view.UpdatedUtc);
        });
        Assert.All(rules, rule => Assert.Equal(created, rule.CreatedUtc));
        Assert.Equal((ubuntu, "Finance laptops", "alice"), (views[dellExact].SequenceId, views[dellExact].Description, views[dellExact].UpdatedBy));
        Assert.Equal((windows, (string?)null, (string?)null), (views[tieLow].SequenceId, views[tieLow].Description, views[tieLow].UpdatedBy));

        // The assignment rules stay until the rules code moves over, and a run still names the rule that chose it.
        Assert.Equal(10, await database.AssignmentRules.CountAsync(cancellationToken));
        Deployment old = await database.Deployments.AsNoTracking().SingleAsync(d => d.Id == run, cancellationToken);
        Assert.Equal("MAC address 00:15:5D:01:02:03", (await database.Rules.AsNoTracking().SingleAsync(r => r.Id == old.RuleId, cancellationToken)).Name);
        Assert.Equal(
            ((string?)null, (string?)null, (string?)null, false, (Guid?)null, (string?)null),
            (old.Answers, old.Values, old.Variables, old.InputsPending, old.PauseStepId, old.PauseMessage));
        DeploymentStep node = await database.DeploymentSteps.AsNoTracking().SingleAsync(s => s.DeploymentId == run, cancellationToken);
        Assert.Equal(((Guid?)null, 0, 0, 0, (IfBranch?)null, (string?)null), (node.ParentId, node.Depth, node.Pass, node.Iteration, node.Branch, node.Evaluation));
        Assert.Null((await database.Machines.AsNoTracking().SingleAsync(m => m.Id == machine, cancellationToken)).Facts);
    }

    private static RuleView View(Rule rule) => new(
        rule.Id,
        rule.Position,
        rule.Name,
        rule.Description,
        rule.Enabled,
        rule.When is null ? null : JsonSerializer.Deserialize(rule.When, DdtJsonContext.Default.ConditionNode),
        rule.TaskSequenceId,
        null,
        JsonSerializer.Deserialize(rule.Values, DdtJsonContext.Default.IReadOnlyListNamedValue)!,
        JsonSerializer.Deserialize(rule.RoleIds, DdtJsonContext.Default.IReadOnlyListGuid)!,
        rule.Revision,
        [],
        0,
        rule.UpdatedUtc,
        rule.UpdatedByName);

    private static TestCondition Test(string variable, ConditionOperator comparison, string value) => new(variable, comparison, value);

    private static AllCondition All(params ConditionNode[] parts) => new() { Parts = parts };

    // Records compare their lists by reference, so conditions are compared as the contracts write them.
    private static string Json(ConditionNode? node) => JsonSerializer.Serialize(node, DdtJsonContext.Default.ConditionNode);

    private static (MachineState, int, Guid?, Guid?) Facts(Machine machine) =>
        (machine.State, machine.TokenGeneration, machine.ActiveDeploymentId, machine.LastDeploymentId);
}
