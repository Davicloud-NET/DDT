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
using DDT.Core.Sequences;
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

    // What JSON and SQL have to escape.
    private const string QuotedMaker = "ACME \"Quote\" \\ Co";
    private const string QuotedModel = "X \"1\"";
    private const string QuotedKey = "model:ACME \"QUOTE\" \\ CO|X \"1\"";

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

        ImageRuns seeded = new();
        await SeedImageRunsAsync(database, seeded, cancellationToken);
        await database.Database.MigrateAsync(cancellationToken);
        database.ChangeTracker.Clear();

        Dictionary<Guid, Deployment> deployments = await database.Deployments.AsNoTracking().ToDictionaryAsync(d => d.Id, cancellationToken);
        Dictionary<Guid, Machine> machines = await database.Machines.AsNoTracking().ToDictionaryAsync(m => m.Id, cancellationToken);

        Deployment failed = deployments[seeded.Running];
        Assert.Equal(DeploymentState.Failed, failed.State);
        Assert.Equal(
            "DDT was upgraded to task sequences while this image was being installed. Assign a task sequence to install the machine again.",
            failed.Error);
        Assert.NotNull(failed.FinishedUtc);
        Assert.Equal("Windows 11 Pro", failed.Title);
        Assert.Equal(0, failed.StepCount);
        Assert.Null(failed.CurrentPhase);
        Assert.Null(failed.TaskSequenceId);

        Deployment cancelled = deployments[seeded.Assigned];
        Assert.Equal(DeploymentState.Cancelled, cancelled.State);
        Assert.Equal("DDT was upgraded to task sequences before this image was installed. Assign a task sequence instead.", cancelled.Error);

        Assert.Equal(DeploymentState.Done, deployments[seeded.Finished].State);
        Assert.Null(deployments[seeded.Finished].Error);
        Assert.Equal("Windows 10 Pro", deployments[seeded.Older].Title);

        // The image column became the sequence column, and no image id may be taken for a sequence's.
        Assert.All(deployments.Values, deployment => Assert.Null(deployment.TaskSequenceId));
        Assert.Equal(2, await database.Images.CountAsync(i => i.Id == seeded.Windows11 || i.Id == seeded.Windows10, cancellationToken));

        Assert.Equal((MachineState.Failed, 4, (Guid?)null, (Guid?)seeded.Running), Facts(machines[seeded.Deploying]));
        Assert.Equal((MachineState.Pending, 2, (Guid?)null, (Guid?)seeded.Assigned), Facts(machines[seeded.Waiting]));
        Assert.Equal((MachineState.Done, 2, (Guid?)null, (Guid?)seeded.Finished), Facts(machines[seeded.Done]));

        // Lines from before kept only the agent's time, uncorrected.
        MachineLogLine line = await database.MachineLogLines.AsNoTracking().SingleAsync(l => l.MachineId == seeded.Deploying, cancellationToken);
        Assert.Equal(seeded.Earlier, line.AgentTimestampUtc);
        Assert.Equal(seeded.Earlier, line.TimestampUtc);
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

        SequenceRun seeded = new();
        await SeedSequenceRunAsync(database, seeded, cancellationToken);
        await database.Database.MigrateAsync(cancellationToken);
        database.ChangeTracker.Clear();

        Assert.Null((await database.Machines.AsNoTracking().SingleAsync(m => m.Id == seeded.Machine, cancellationToken)).SecureBootEnabled);
        Image windows = await database.Images.AsNoTracking().SingleAsync(i => i.Id == seeded.Image, cancellationToken);
        Assert.Equal(
            (ImageKind.Wim, "Windows 11 Pro", (ImageBootCapability?)null, (string?)null, (string?)null),
            (windows.Kind, windows.Name, windows.BootCapability, windows.BootDetail, windows.SourceSha256));
        Deployment deployment = await database.Deployments.AsNoTracking().SingleAsync(d => d.Id == seeded.Run, cancellationToken);
        Assert.Equal((DeploymentState.Done, false), (deployment.State, deployment.AllowSecureBootMismatch));
        DeploymentArtifact artifact = await database.DeploymentArtifacts.AsNoTracking().SingleAsync(a => a.DeploymentId == seeded.Run, cancellationToken);
        Assert.Equal((seeded.Image, (ImageBootCapability?)null), (artifact.SourceId, artifact.BootCapability));

        // A raw disk image is found by the SHA-256 of its disk, as a second upload of that disk is.
        database.Images.Add(new Image
        {
            Id = Guid.NewGuid(),
            Name = "noble-server-cloudimg-amd64",
            Kind = ImageKind.RawDisk,
            Sha256 = "bb",
            SizeBytes = 1,
            InstalledBytes = 4,
            UploadedUtc = seeded.Now,
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

        LegacyRules seeded = new();
        Assert.True(seeded.TieLow.CompareTo(seeded.TieHigh) < 0);
        await SeedLegacyRulesAsync(database, seeded, cancellationToken);
        await database.Database.MigrateAsync(cancellationToken);
        database.ChangeTracker.Clear();

        List<Rule> rules = await database.Rules.AsNoTracking().OrderBy(r => r.Position).ToListAsync(cancellationToken);

        Assert.Equal(
            [seeded.MacLow, seeded.MacHigh, seeded.DellExact, seeded.AnyExact, seeded.TieLow, seeded.TieHigh, seeded.Quoted, seeded.DellPrefix, seeded.AnyPrefix, seeded.ShortPrefix],
            rules.Select(r => r.Id));
        Assert.Equal(Enumerable.Range(0, 10), rules.Select(r => r.Position));
        Assert.Equal(
            [
                "MAC address 00:15:5D:01:02:03",
                "MAC address 00:15:5D:01:02:FF",
                "Model Dell Inc. Latitude 5440",
                "Model Latitude 5440 of any maker",
                "Model OptiPlex 7020 of any maker",
                "Model OptiPlex 7010 of any maker",
                $"Model {QuotedMaker} {QuotedModel}",
                "Model Dell Inc. Latitude 7*",
                "Model Latitude 7* of any maker",
                "Model Lat* of any maker",
            ],
            rules.Select(r => r.Name));

        // Read through the contracts, as the rules page reads a rule.
        Dictionary<Guid, RuleView> views = rules.ToDictionary(
            r => r.Id,
            r => JsonSerializer.Deserialize(JsonSerializer.Serialize(View(r), DdtJsonContext.Default.RuleView), DdtJsonContext.Default.RuleView)!);

        AssertConditions(views, seeded);
        AssertKeptFields(views, rules, seeded);
        AssertChoices(rules, seeded);
        await AssertOldRunAsync(database, seeded, cancellationToken);
    }

    private static async Task SeedImageRunsAsync(DdtDbContext database, ImageRuns seeded, CancellationToken cancellationToken)
    {
        await database.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO ddt."Machines" ("Id", "SmbiosUuid", "PrimaryMac", "MacAddresses", "State", "TokenGeneration", "FirstSeenUtc", "LastSeenUtc", "ActiveDeploymentId")
            VALUES
                ({seeded.Deploying}, 'uuid-1', '020000000001', '020000000001', 'Deploying', 3, {seeded.Earlier}, {seeded.Later}, {seeded.Running}),
                ({seeded.Waiting}, 'uuid-2', '020000000002', '020000000002', 'Pending', 1, {seeded.Earlier}, {seeded.Later}, {seeded.Assigned}),
                ({seeded.Done}, 'uuid-3', '020000000003', '020000000003', 'Done', 2, {seeded.Earlier}, {seeded.Later}, NULL)
            """,
            cancellationToken);

        // Every image deployment named its image, and the library keeps the images.
        await database.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO ddt."Images" ("Id", "Name", "Kind", "Sha256", "SizeBytes", "WimIndex", "InstalledBytes", "UploadedUtc")
            VALUES
                ({seeded.Windows11}, 'Windows 11 Pro', 'Wim', 'aa', 1, 6, 4, {seeded.Earlier}),
                ({seeded.Windows10}, 'Windows 10 Pro', 'Wim', 'bb', 1, 1, 4, {seeded.Earlier})
            """,
            cancellationToken);

        await database.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO ddt."Deployments" ("Id", "MachineId", "ImageId", "ImageName", "Sha256", "SizeBytes", "WimIndex", "InstalledBytes", "State", "Step", "Percent", "Source", "CreatedUtc", "UpdatedUtc")
            VALUES
                ({seeded.Running}, {seeded.Deploying}, {seeded.Windows11}, 'Windows 11 Pro', 'aa', 1, 6, 4, 'Running', 'Apply', 40, 'Web', {seeded.Later}, {seeded.Later}),
                ({seeded.Assigned}, {seeded.Waiting}, {seeded.Windows11}, 'Windows 11 Pro', 'aa', 1, 6, 4, 'Assigned', NULL, 0, 'Web', {seeded.Later}, {seeded.Later}),
                ({seeded.Older}, {seeded.Done}, {seeded.Windows10}, 'Windows 10 Pro', 'bb', 1, 1, 4, 'Done', 'Reboot', 100, 'Console', {seeded.Earlier}, {seeded.Earlier}),
                ({seeded.Finished}, {seeded.Done}, {seeded.Windows11}, 'Windows 11 Pro', 'aa', 1, 6, 4, 'Done', 'Reboot', 100, 'Web', {seeded.Later}, {seeded.Later})
            """,
            cancellationToken);

        await database.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO ddt."MachineLogLines" ("MachineId", "TimestampUtc", "ReceivedUtc", "Level", "Message")
            VALUES ({seeded.Deploying}, {seeded.Earlier}, {seeded.Later}, 'Information', 'Applying the image')
            """,
            cancellationToken);
    }

    private static async Task SeedSequenceRunAsync(DdtDbContext database, SequenceRun seeded, CancellationToken cancellationToken)
    {
        await database.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO ddt."Machines" ("Id", "SmbiosUuid", "PrimaryMac", "MacAddresses", "State", "TokenGeneration", "SequenceVersion", "FirstSeenUtc", "LastSeenUtc")
            VALUES ({seeded.Machine}, 'uuid-1', '020000000001', '020000000001', 'Done', 1, 1, {seeded.Now}, {seeded.Now})
            """,
            cancellationToken);
        await database.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO ddt."Images" ("Id", "Name", "Kind", "Sha256", "SizeBytes", "WimIndex", "InstalledBytes", "UploadedUtc")
            VALUES ({seeded.Image}, 'Windows 11 Pro', 'Wim', 'aa', 1, 6, 4, {seeded.Now})
            """,
            cancellationToken);
        await database.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO ddt."Deployments" ("Id", "MachineId", "Title", "State", "Source", "Percent", "StepCount", "CreatedUtc", "UpdatedUtc")
            VALUES ({seeded.Run}, {seeded.Machine}, 'Install Windows', 'Done', 'Web', 100, 1, {seeded.Now}, {seeded.Now})
            """,
            cancellationToken);
        await database.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO ddt."DeploymentArtifacts" ("DeploymentId", "StepId", "Kind", "SourceId", "Name", "Sha256", "SizeBytes", "ExpandedBytes", "WimIndex")
            VALUES ({seeded.Run}, {seeded.Step}, 'Image', {seeded.Image}, 'Windows 11 Pro', 'aa', 1, 4, 6)
            """,
            cancellationToken);
    }

    private static async Task SeedLegacyRulesAsync(DdtDbContext database, LegacyRules seeded, CancellationToken cancellationToken)
    {
        string definition = """{"version":1,"steps":[]}""";

        await database.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO ddt."TaskSequences" ("Id", "Name", "NormalizedName", "Definition", "Revision", "CreatedUtc", "UpdatedUtc")
            VALUES
                ({seeded.Windows}, 'Install Windows', 'INSTALL WINDOWS', {definition}, 1, {seeded.Created}, {seeded.Created}),
                ({seeded.Ubuntu}, 'Install Ubuntu', 'INSTALL UBUNTU', {definition}, 1, {seeded.Created}, {seeded.Created})
            """,
            cancellationToken);
        await database.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO ddt."AssignmentRules" ("Id", "Kind", "MatchKey", "Mac", "Manufacturer", "Model", "TaskSequenceId", "Description", "CreatedUtc", "UpdatedUtc", "UpdatedByName")
            VALUES
                ({seeded.ShortPrefix}, 'Model', 'model:|LAT*', NULL, NULL, 'Lat*', {seeded.Windows}, NULL, {seeded.Created}, {seeded.Updated}, 'alice'),
                ({seeded.AnyPrefix}, 'Model', 'model:|LATITUDE 7*', NULL, NULL, 'Latitude 7*', {seeded.Windows}, NULL, {seeded.Created}, {seeded.Updated}, 'alice'),
                ({seeded.DellPrefix}, 'Model', 'model:DELL INC.|LATITUDE 7*', NULL, 'Dell Inc.', 'Latitude 7*', {seeded.Ubuntu}, NULL, {seeded.Created}, {seeded.Updated}, 'alice'),
                ({seeded.Quoted}, 'Model', {QuotedKey}, NULL, {QuotedMaker}, {QuotedModel}, {seeded.Windows}, NULL, {seeded.Created}, {seeded.Updated}, 'alice'),
                ({seeded.AnyExact}, 'Model', 'model:|LATITUDE 5440', NULL, NULL, 'Latitude 5440', {seeded.Windows}, NULL, {seeded.Created}, {seeded.Updated}, 'alice'),
                ({seeded.DellExact}, 'Model', 'model:DELL INC.|LATITUDE 5440', NULL, 'Dell Inc.', 'Latitude 5440', {seeded.Ubuntu}, 'Finance laptops', {seeded.Created}, {seeded.Updated}, 'alice'),
                ({seeded.TieHigh}, 'Model', 'model:|OPTIPLEX 7010', NULL, NULL, 'OptiPlex 7010', {seeded.Windows}, NULL, {seeded.Created}, {seeded.Updated}, NULL),
                ({seeded.TieLow}, 'Model', 'model:|OPTIPLEX 7020', NULL, NULL, 'OptiPlex 7020', {seeded.Windows}, NULL, {seeded.Created}, {seeded.Updated}, NULL),
                ({seeded.MacHigh}, 'Mac', 'mac:00155D0102FF', '00155D0102FF', NULL, NULL, {seeded.Ubuntu}, NULL, {seeded.Created}, {seeded.Updated}, 'alice'),
                ({seeded.MacLow}, 'Mac', 'mac:00155D010203', '00155D010203', NULL, NULL, {seeded.Windows}, 'The lab PC', {seeded.Created}, {seeded.Updated}, 'alice')
            """,
            cancellationToken);

        // A run from before trees, which a rule chose.
        await database.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO ddt."Machines" ("Id", "SmbiosUuid", "PrimaryMac", "MacAddresses", "State", "TokenGeneration", "SequenceVersion", "FirstSeenUtc", "LastSeenUtc")
            VALUES ({seeded.Machine}, 'uuid-1', '00155D010203', '00155D010203', 'Done', 1, 2, {seeded.Created}, {seeded.Created})
            """,
            cancellationToken);
        await database.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO ddt."Deployments" ("Id", "MachineId", "TaskSequenceId", "RuleId", "Title", "State", "Source", "Percent", "StepCount", "CreatedUtc", "UpdatedUtc")
            VALUES ({seeded.Run}, {seeded.Machine}, {seeded.Windows}, {seeded.MacLow}, 'Install Windows', 'Done', 'Rule', 100, 1, {seeded.Created}, {seeded.Created})
            """,
            cancellationToken);
        await database.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO ddt."DeploymentSteps" ("DeploymentId", "StepId", "Index", "Name", "Kind", "Phase", "State", "Percent")
            VALUES ({seeded.Run}, {seeded.Step}, 0, 'Partition', 'partition', 'WindowsPE', 'Done', 100)
            """,
            cancellationToken);
    }

    private static void AssertConditions(Dictionary<Guid, RuleView> views, LegacyRules seeded)
    {
        Assert.Equal(Json(Test(MachineVariableNames.MacAddress, ConditionOperator.Equals, "00:15:5D:01:02:03")), Json(views[seeded.MacLow].When));
        Assert.Equal(Json(Test(MachineVariableNames.MacAddress, ConditionOperator.Equals, "00:15:5D:01:02:FF")), Json(views[seeded.MacHigh].When));
        Assert.Equal(
            Json(All(
                Test(MachineVariableNames.Manufacturer, ConditionOperator.Equals, "Dell Inc."),
                Test(MachineVariableNames.Model, ConditionOperator.Equals, "Latitude 5440"))),
            Json(views[seeded.DellExact].When));
        Assert.Equal(Json(All(Test(MachineVariableNames.Model, ConditionOperator.Equals, "Latitude 5440"))), Json(views[seeded.AnyExact].When));
        Assert.Equal(
            Json(All(
                Test(MachineVariableNames.Manufacturer, ConditionOperator.Equals, QuotedMaker),
                Test(MachineVariableNames.Model, ConditionOperator.Equals, QuotedModel))),
            Json(views[seeded.Quoted].When));
        Assert.Equal(
            Json(All(
                Test(MachineVariableNames.Manufacturer, ConditionOperator.Equals, "Dell Inc."),
                Test(MachineVariableNames.Model, ConditionOperator.Matches, "Latitude 7*"))),
            Json(views[seeded.DellPrefix].When));
        Assert.Equal(Json(All(Test(MachineVariableNames.Model, ConditionOperator.Matches, "Lat*"))), Json(views[seeded.ShortPrefix].When));
    }

    private static void AssertKeptFields(Dictionary<Guid, RuleView> views, List<Rule> rules, LegacyRules seeded)
    {
        Assert.All(views.Values, view =>
        {
            Assert.True(view.Enabled);
            Assert.Empty(view.Values);
            Assert.Empty(view.RoleIds);
            Assert.Equal(1, view.Revision);
            Assert.Equal(seeded.Updated, view.UpdatedUtc);
        });
        Assert.All(rules, rule => Assert.Equal(seeded.Created, rule.CreatedUtc));
        Assert.Equal(
            (seeded.Ubuntu, "Finance laptops", "alice"),
            (views[seeded.DellExact].SequenceId, views[seeded.DellExact].Description, views[seeded.DellExact].UpdatedBy));
        Assert.Equal(
            (seeded.Windows, (string?)null, (string?)null),
            (views[seeded.TieLow].SequenceId, views[seeded.TieLow].Description, views[seeded.TieLow].UpdatedBy));
    }

    // Walked from the top, the copies choose for each machine the rule the assignment rules chose.
    private static void AssertChoices(List<Rule> rules, LegacyRules seeded)
    {
        RuleBook book = RuleBook.From(rules, []);
        Assert.All(book.Rules, rule => Assert.Empty(rule.Problems));

        Guid? Chosen(string manufacturer, string model, params string[] macs) =>
            book.Match(new MachineVariables(manufacturer, model, null, "uuid", macs, null, SequencePhase.WindowsPE), []).Chooser?.Rule.Id;

        Assert.Equal(seeded.MacLow, Chosen("Dell Inc.", "Latitude 5440", "00155D010203"));
        Assert.Equal(seeded.DellExact, Chosen("DELL INC.", "latitude  5440"));

        // Only where rules name two addresses of one machine, the higher rule now wins over the one for its primary address.
        Assert.Equal(seeded.MacLow, Chosen("Dell Inc.", "Latitude 5440", "00155D0102FF", "00155D010203"));
        Assert.Equal(seeded.AnyExact, Chosen("HP", "Latitude 5440"));
        Assert.Equal(seeded.DellPrefix, Chosen("Dell Inc.", "Latitude 7440"));
        Assert.Equal(seeded.AnyPrefix, Chosen("LENOVO", "Latitude 7440"));
        Assert.Equal(seeded.ShortPrefix, Chosen("Dell Inc.", "Latitude 9440"));
        Assert.Equal(seeded.Quoted, Chosen(QuotedMaker, QuotedModel));
        Assert.Null(Chosen("To Be Filled By O.E.M.", "To Be Filled By O.E.M."));
    }

    // The assignment rules are gone once copied, and a run still names the rule that chose it.
    private static async Task AssertOldRunAsync(DdtDbContext database, LegacyRules seeded, CancellationToken cancellationToken)
    {
        Assert.False(await database.Database
            .SqlQuery<bool>($"""SELECT to_regclass('ddt."AssignmentRules"') IS NOT NULL AS "Value" """)
            .SingleAsync(cancellationToken));
        Deployment old = await database.Deployments.AsNoTracking().SingleAsync(d => d.Id == seeded.Run, cancellationToken);
        Assert.Equal("MAC address 00:15:5D:01:02:03", (await database.Rules.AsNoTracking().SingleAsync(r => r.Id == old.RuleId, cancellationToken)).Name);
        Assert.Equal(
            ((string?)null, (string?)null, (string?)null, false, (Guid?)null, (string?)null),
            (old.Answers, old.Values, old.Variables, old.InputsPending, old.PauseStepId, old.PauseMessage));
        DeploymentStep node = await database.DeploymentSteps.AsNoTracking().SingleAsync(s => s.DeploymentId == seeded.Run, cancellationToken);
        Assert.Equal(((Guid?)null, 0, 0, 0, (IfBranch?)null, (string?)null), (node.ParentId, node.Depth, node.Pass, node.Iteration, node.Branch, node.Evaluation));
        Assert.Null((await database.Machines.AsNoTracking().SingleAsync(m => m.Id == seeded.Machine, cancellationToken)).Facts);
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

    // Three machines of an image library, deploying, waiting and done, with a run each and an older run of the one done.
    private sealed record ImageRuns
    {
        public Guid Deploying { get; } = Guid.NewGuid();

        public Guid Waiting { get; } = Guid.NewGuid();

        public Guid Done { get; } = Guid.NewGuid();

        public Guid Running { get; } = Guid.NewGuid();

        public Guid Assigned { get; } = Guid.NewGuid();

        public Guid Older { get; } = Guid.NewGuid();

        public Guid Finished { get; } = Guid.NewGuid();

        public Guid Windows11 { get; } = Guid.NewGuid();

        public Guid Windows10 { get; } = Guid.NewGuid();

        public DateTimeOffset Earlier { get; } = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

        public DateTimeOffset Later => Earlier.AddHours(1);
    }

    // A machine that a task sequence installed from an image.
    private sealed record SequenceRun
    {
        public Guid Machine { get; } = Guid.NewGuid();

        public Guid Image { get; } = Guid.NewGuid();

        public Guid Run { get; } = Guid.NewGuid();

        public Guid Step { get; } = Guid.NewGuid();

        public DateTimeOffset Now { get; } = new(2026, 9, 24, 8, 0, 0, TimeSpan.Zero);
    }

    // Each rule's id is lower than those of the rules it has to come after, so only the ordering can put it there. The
    // OptiPlex rules tie on everything but their ids, which differ in the first byte only: a signed comparison would put
    // 80... first, and .NET and PostgreSQL both put 10... first.
    private sealed record LegacyRules
    {
        public Guid Windows { get; } = Guid.NewGuid();

        public Guid Ubuntu { get; } = Guid.NewGuid();

        public Guid ShortPrefix { get; } = Guid.Parse("00000000-0000-0000-0000-000000000001");

        public Guid AnyPrefix { get; } = Guid.Parse("00000000-0000-0000-0000-000000000002");

        public Guid DellPrefix { get; } = Guid.Parse("00000000-0000-0000-0000-000000000003");

        public Guid Quoted { get; } = Guid.Parse("00000000-0000-0000-0000-000000000004");

        public Guid AnyExact { get; } = Guid.Parse("00000000-0000-0000-0000-000000000005");

        public Guid DellExact { get; } = Guid.Parse("00000000-0000-0000-0000-000000000006");

        public Guid MacLow { get; } = Guid.Parse("00000000-0000-0000-0000-00000000000e");

        public Guid MacHigh { get; } = Guid.Parse("00000000-0000-0000-0000-00000000000f");

        public Guid TieLow { get; } = Guid.Parse("10000000-0000-0000-0000-000000000000");

        public Guid TieHigh { get; } = Guid.Parse("80000000-0000-0000-0000-000000000000");

        public Guid Machine { get; } = Guid.NewGuid();

        public Guid Run { get; } = Guid.NewGuid();

        public Guid Step { get; } = Guid.NewGuid();

        public DateTimeOffset Created { get; } = new(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);

        public DateTimeOffset Updated => Created.AddDays(1);
    }
}
