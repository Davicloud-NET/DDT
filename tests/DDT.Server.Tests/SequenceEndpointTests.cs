// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using DDT.Contracts.Sequences;
using DDT.Server.Authentication;
using DDT.Server.Data;
using DDT.Server.Images;
using DDT.Server.Machines;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace DDT.Server.Tests;

public sealed class SequenceEndpointTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private static Task<T> ReadAsync<T>(HttpResponseMessage response) => RegisteredMachine.ReadAsync<T>(response);

    private static string StepJson(Guid id, string kind) =>
        $$"""{"id":"{{id}}","name":"Restart","kind":"{{kind}}"}""";

    private Task<List<AuditEvent>> AuditAsync(Guid sequenceId)
    {
        string subject = sequenceId.ToString("D");

        return application.QueryAsync(database => database.AuditEvents
            .AsNoTracking()
            .Where(e => e.SubjectId == subject)
            .OrderBy(e => e.Id)
            .ToListAsync(TestContext.Current.CancellationToken));
    }

    private async Task<SequenceValidation> ValidateAsync(SequenceDefinition definition) =>
        await ReadAsync<SequenceValidation>(
            await (await application.AdministratorAsync()).PostAsync($"{SequenceRequests.Sequences}/validate", definition));

    [Fact]
    public async Task OnlyAnAdministratorWritesAndEveryoneSignedInReads()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using SignedInClient viewer = await application.SignInAsync(DdtRoleNames.Viewer);
        using SignedInClient operatorClient = await application.SignInAsync(DdtRoleNames.Operator);
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.Minimal(Guid.Empty));

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(SequenceRequests.Sequences)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"{SequenceRequests.Sequences}/{sequence.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.CreateSequenceAsync(sequence.Definition)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.SaveSequenceAsync(sequence)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.DeleteAsync($"{SequenceRequests.Sequences}/{sequence.Id}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await operatorClient.PostAsync($"{SequenceRequests.Sequences}/validate", sequence.Definition)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.GetAsync($"{SequenceRequests.Sequences}/templates")).StatusCode);

        using HttpClient anonymous = application.CreateClient();
        HttpResponseMessage listed = await anonymous.GetAsync(new Uri(SequenceRequests.Sequences, UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, listed.StatusCode);
    }

    [Fact]
    public async Task CreatesASequenceAndListsItWithItsProblems()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string name = $"Lab {Guid.NewGuid():N}";
        SequenceDefinition definition = SequenceRequests.Minimal(Guid.Empty);

        HttpResponseMessage created = await administrator.PostAsync(
            SequenceRequests.Sequences,
            new CreateSequenceRequest($"  {name} ", "For the lab.\0", definition));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        SequenceView view = await ReadAsync<SequenceView>(created);
        Assert.Equal($"/api/sequences/{view.Id}", created.Headers.Location?.OriginalString);
        Assert.Equal(name, view.Name);
        Assert.Equal("For the lab.", view.Description);
        Assert.Equal(1, view.Revision);
        // Stored with the lowest version its kinds need, so agents from before raw disk images still run it.
        Assert.Equal(SequenceDefinition.CurrentVersion, definition.Version);
        Assert.Equal(SequenceRequests.Json(definition with { Version = 1 }), SequenceRequests.Json(view.Definition));
        Assert.Equal(new[] { SequencePhase.WindowsPE, SequencePhase.WindowsPE }, view.StepPhases);
        Assert.StartsWith("administrator-", view.UpdatedBy, StringComparison.Ordinal);

        // It's saved even though no image is chosen yet. The problem keeps it from running, not from being stored.
        Assert.Equal(new SequenceProblem(definition.Steps[1].Id, "imageId", "Choose the image to apply."), Assert.Single(view.Problems));
        Assert.Empty(view.Warnings);

        IReadOnlyList<SequenceSummary> listed = await ReadAsync<IReadOnlyList<SequenceSummary>>(await administrator.GetAsync(SequenceRequests.Sequences));
        Assert.Equal(
            new SequenceSummary(view.Id, name, "For the lab.", 1, 2, 1, 0, true, false, false, view.UpdatedUtc, view.UpdatedBy),
            Assert.Single(listed, s => s.Id == view.Id));

        AuditEvent audit = Assert.Single(await AuditAsync(view.Id));
        Assert.Equal(AuditActions.SequenceCreated, audit.Action);
        Assert.Equal($"{name}, 2 steps.", audit.Detail);
    }

    [Fact]
    public async Task SavesOverTheRevisionItWasGivenAndRefusesAStaleOneWithTheCurrentSequence()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceView first = await administrator.CreatedSequenceAsync(SequenceRequests.Minimal(Guid.Empty));
        RebootStep restart = new() { Id = Guid.NewGuid(), Name = "Restart" };

        HttpResponseMessage saved = await administrator.SaveSequenceAsync(first, first.Definition with { Steps = [.. first.Definition.Steps, restart] });
        SequenceView second = await ReadAsync<SequenceView>(saved);

        Assert.Equal(2, second.Revision);
        Assert.Equal(3, second.Definition.Steps.Count);

        // Another administrator's editor still holds revision 1.
        HttpResponseMessage stale = await administrator.SaveSequenceAsync(first, name: $"Mine {Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        SequenceView current = (await stale.Content.ReadFromJsonAsync<SequenceView>(TestJson.Options, TestContext.Current.CancellationToken))!;
        Assert.Equal(2, current.Revision);
        Assert.Equal(first.Name, current.Name);
        Assert.Equal(SequenceRequests.Json(second.Definition), SequenceRequests.Json(current.Definition));

        AuditEvent changed = (await AuditAsync(first.Id))[^1];
        Assert.Equal(AuditActions.SequenceChanged, changed.Action);
        Assert.Equal($"Revision 2. Added Restart ({restart.Id:D}).", changed.Detail);
    }

    [Fact]
    public async Task AnUnchangedSaveKeepsTheRevisionAndRecordsNothing()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.Minimal(Guid.Empty));

        SequenceView again = await ReadAsync<SequenceView>(await administrator.SaveSequenceAsync(sequence));

        Assert.Equal(1, again.Revision);
        Assert.Single(await AuditAsync(sequence.Id));
    }

    [Fact]
    public async Task RecordsWhichStepsChangedAndTheHashOfAChangedScript()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        RunScriptStep script = new() { Id = Guid.NewGuid(), Name = "Hello", Script = "echo hello" };
        RebootStep restart = new() { Id = Guid.NewGuid(), Name = "Restart" };
        string before = $"Before {Guid.NewGuid():N}";
        string after = $"After {Guid.NewGuid():N}";
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.Definition(script, restart), before);

        RunScriptStep changed = script with { Script = "echo changed" };
        await ReadAsync<SequenceView>(await administrator.SaveSequenceAsync(sequence, SequenceRequests.Definition(changed), after));

        string hash = Convert.ToHexStringLower(SHA256.HashData("echo changed"u8.ToArray()));

        Assert.Equal(
            $"Revision 2. Renamed from {before} to {after}. Removed Restart ({restart.Id:D}). Changed Hello ({script.Id:D}), script SHA-256 {hash}.",
            (await AuditAsync(sequence.Id))[^1].Detail);
    }

    [Fact]
    public async Task SavesASequenceWithProblemsAndReturnsThem()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        ApplyImageStep apply = new() { Id = Guid.NewGuid(), Name = "Apply", ImageId = Guid.NewGuid() };

        SequenceView view = await administrator.CreatedSequenceAsync(SequenceRequests.Definition(apply));

        SequenceProblem[] expected =
        [
            new(apply.Id, null, "The image can be applied only after a step that partitions the disk."),
            new(apply.Id, "imageId", "The image is no longer in the library. Choose another image."),
        ];
        Assert.Equal(expected, view.Problems);
    }

    [Theory]
    [InlineData("""{"version":1,"steps":[{"id":"8d4c2c5e-8a0f-4d6c-9d38-3f7ad4d4c1a1","name":"Restart"}]}""")]
    [InlineData("""{"version":1,"steps":[{"id":"8d4c2c5e-8a0f-4d6c-9d38-3f7ad4d4c1a1","name":"Restart","kind":"format"}]}""")]
    [InlineData("""{"version":1,"steps":[{"name":"Restart","kind":"reboot"}]}""")]
    [InlineData("""{"version":1,"steps":[null]}""")]
    [InlineData("""{"version":3,"steps":[{"kind":"group","id":"8d4c2c5e-8a0f-4d6c-9d38-3f7ad4d4c1a1","name":"Group","steps":[null]}]}""")]
    [InlineData("""{"version":1}""")]
    [InlineData("""{"version":1,"steps":[""")]
    [InlineData("null")]
    public async Task RefusesADocumentItCannotStoreWith400(string definition)
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string created = $$"""{"name":"Broken {{Guid.NewGuid():N}}","definition":{{definition}}}""";

        HttpResponseMessage create = await administrator.SendJsonAsync(HttpMethod.Post, SequenceRequests.Sequences, created);
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        Assert.StartsWith("The sequence is not a document DDT can read.", await TestDatabase.TitleAsync(create), StringComparison.Ordinal);

        HttpResponseMessage validate = await administrator.SendJsonAsync(HttpMethod.Post, $"{SequenceRequests.Sequences}/validate", definition);
        Assert.Equal(HttpStatusCode.BadRequest, validate.StatusCode);

        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.Minimal(Guid.Empty));
        string saved = $$"""{"revision":1,"name":"{{sequence.Name}}","definition":{{definition}}}""";
        HttpResponseMessage save = await administrator.SendJsonAsync(HttpMethod.Put, $"{SequenceRequests.Sequences}/{sequence.Id}", saved);
        Assert.Equal(HttpStatusCode.BadRequest, save.StatusCode);
    }

    [Fact]
    public async Task RefusesMoreStepsThanItStoresAndABodyThatIsNotJson()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceStep[] restarts = [.. Enumerable.Range(0, 200).Select(i => new RebootStep { Id = Guid.NewGuid(), Name = $"Restart {i}" })];
        GroupStep group = new() { Id = Guid.NewGuid(), Name = "Restarts", Steps = [.. restarts.Select(step => step with { Id = Guid.NewGuid() })] };

        // Every node of the tree counts, both the group and the steps inside it.
        HttpResponseMessage tooMany = await administrator.CreateSequenceAsync(SequenceRequests.Definition([.. restarts, group]));
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        Assert.Equal(
            "A sequence can have at most 400 steps, groups, IFs and repeats together.",
            (await tooMany.Content.ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken))?.Detail);

        HttpResponseMessage most = await administrator.CreateSequenceAsync(SequenceRequests.Definition([.. restarts[1..], group]));
        Assert.Equal(HttpStatusCode.Created, most.StatusCode);

        using HttpRequestMessage text = new(HttpMethod.Post, new Uri(SequenceRequests.Sequences, UriKind.Relative))
        {
            Content = new StringContent("{}"),
        };
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await administrator.SendAsync(text, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task ReadsAStepWhoseKindComesAfterItsOtherMembers()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        Guid stepId = Guid.NewGuid();
        string body = $$"""{"definition":{"steps":[{{StepJson(stepId, "reboot")}}],"version":1},"name":"Reordered {{Guid.NewGuid():N}}"}""";

        HttpResponseMessage created = await administrator.SendJsonAsync(HttpMethod.Post, SequenceRequests.Sequences, body);

        RebootStep step = Assert.IsType<RebootStep>(Assert.Single((await ReadAsync<SequenceView>(created)).Definition.Steps));
        Assert.Equal(stepId, step.Id);
    }

    // DdtJsonContext's AllowOutOfOrderMetadataProperties doesn't reach the host's or the hub's JSON options. Both read
    // requests, so each sets it too, and a kind after the other members still reads.
    [Fact]
    public void TheHostAndTheHubReadAKindAfterTheOtherMembers()
    {
        string json = $$"""{"version":1,"steps":[{{StepJson(Guid.NewGuid(), "reboot")}}]}""";
        JsonSerializerOptions host = application.Services.GetRequiredService<IOptions<HttpJsonOptions>>().Value.SerializerOptions;
        JsonSerializerOptions hub = application.Services.GetRequiredService<IOptions<JsonHubProtocolOptions>>().Value.PayloadSerializerOptions;

        Assert.IsType<RebootStep>(Assert.Single(JsonSerializer.Deserialize<SequenceDefinition>(json, host)!.Steps));
        Assert.IsType<RebootStep>(Assert.Single(JsonSerializer.Deserialize<SequenceDefinition>(json, hub)!.Steps));
    }

    [Fact]
    public async Task RefusesANameThatIsEmptyTooLongOrTakenInAnyCase()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        string name = $"Taken {Guid.NewGuid():N}";
        SequenceView taken = await administrator.CreatedSequenceAsync(SequenceRequests.Minimal(Guid.Empty), name);
        SequenceView other = await administrator.CreatedSequenceAsync(SequenceRequests.Minimal(Guid.Empty));

        foreach (string refused in new[] { " ", new string('a', 129), "Line\nbreak", name.ToUpperInvariant() })
        {
            HttpResponseMessage response = await administrator.CreateSequenceAsync(taken.Definition, refused);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("name", (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(TestContext.Current.CancellationToken))!.Errors.Keys);
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await administrator.SaveSequenceAsync(other, name: $" {name.ToLowerInvariant()}")).StatusCode);

        // Its own name in another case doesn't count as taken.
        SequenceView renamed = await ReadAsync<SequenceView>(await administrator.SaveSequenceAsync(taken, name: name.ToUpperInvariant()));
        Assert.Equal(name.ToUpperInvariant(), renamed.Name);
    }

    // A node after an IF runs in the phases of both branches.
    // The IF runs in the phase it starts in and in the phases of what it holds.
    [Fact]
    public async Task ShowsThePhasesEveryNodeMayRunIn()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        RunScriptStep inWindows = new() { Id = Guid.NewGuid(), Name = "In Windows", Phase = SequencePhase.Windows, Script = "hostname" };
        RebootStep otherwise = new() { Id = Guid.NewGuid(), Name = "Restart in Windows PE" };
        IfStep choose = new()
        {
            Id = Guid.NewGuid(),
            Name = "If a ThinkPad",
            Test = new TestCondition(MachineVariableNames.FriendlyModel, ConditionOperator.Matches, "ThinkPad*"),
            Then = [inWindows],
            Else = [otherwise],
        };
        RebootStep last = new() { Id = Guid.NewGuid(), Name = "Restart" };
        SequenceDefinition definition = SequenceRequests.Definition(
            new PartitionStep { Id = Guid.NewGuid(), Name = "Partition" },
            new ApplyImageStep { Id = Guid.NewGuid(), Name = "Apply", ImageId = Guid.NewGuid() },
            choose,
            last);

        SequenceView view = await administrator.CreatedSequenceAsync(definition);

        Assert.Equal(
            [
                $"{definition.Steps[0].Id} WindowsPE",
                $"{definition.Steps[1].Id} WindowsPE",
                $"{choose.Id} WindowsPE, Windows",
                $"{inWindows.Id} Windows",
                $"{otherwise.Id} WindowsPE",
                $"{last.Id} WindowsPE, Windows",
            ],
            view.NodePhases!.Select(node => $"{node.NodeId} {string.Join(", ", node.Phases)}"));
        Assert.Equal([SequencePhase.WindowsPE, SequencePhase.WindowsPE, SequencePhase.WindowsPE, SequencePhase.WindowsPE], view.StepPhases);

        IReadOnlyList<SequenceSummary> listed = await ReadAsync<IReadOnlyList<SequenceSummary>>(await administrator.GetAsync(SequenceRequests.Sequences));
        SequenceSummary summary = Assert.Single(listed, s => s.Id == view.Id);
        // The steps of both branches, but not the IF that holds them.
        Assert.Equal(5, summary.StepCount);
        Assert.True(summary.ContinuesInWindows);
    }

    [Fact]
    public async Task ChecksWhatTheStepsReferToOnTheServer()
    {
        Image arm = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096), architecture: "arm64", name: "Arm image");
        SequenceDefinition definition = SequenceRequests.Definition(
            new PartitionStep { Id = Guid.NewGuid(), Name = "Partition" },
            new ApplyImageStep { Id = Guid.NewGuid(), Name = "Apply", ImageId = arm.Id },
            new WriteUnattendStep
            {
                Id = Guid.NewGuid(),
                Name = "Answer file",
                TimeZone = "Middle Earth Standard Time",
                Locale = "de",
                Keyboard = "de-DE;0407:0000040X",
                LocalAdministrator = true,
            },
            new JoinDomainStep { Id = Guid.NewGuid(), Name = "Join", OrganizationalUnit = "LDAP://OU=Lab,DC=corp,DC=example" });
        Guid apply = definition.Steps[1].Id;
        Guid unattend = definition.Steps[2].Id;
        Guid join = definition.Steps[3].Id;

        SequenceValidation validation = await ValidateAsync(definition);

        SequenceProblem[] expected =
        [
            new(apply, "imageId", "Arm image is an arm64 image, and DDT deploys only x64 Windows. Choose an x64 image."),
            new(unattend, "timeZone", "'Middle Earth Standard Time' is not a Windows time zone id. Use a name that tzutil /l lists, such as W. Europe Standard Time."),
            new(unattend, "locale", "'de' is not a language and region that Windows knows. Use a name such as de-DE."),
            new(unattend, "keyboard", "'de-DE;0407:0000040X' is not an input locale. Use a name such as de-DE or a code such as 0407:00000407."),
            new(unattend, "localAdministrator", "No local administrator is set on the Deployment defaults page, so the answer file cannot add one."),
            new(join, null, "No domain is set on the Deployment defaults page, so the machine has no domain to join. Set one there or remove this step."),
            new(join, "organizationalUnit", "Must be a distinguished name without the LDAP:// prefix, such as OU=Workstations,DC=example,DC=com."),
        ];
        Assert.Equal(expected, validation.Problems);
        Assert.Empty(validation.Warnings);
    }

    // Setup would stop at the account page, but an administrator may mean to finish it by hand, so it only warns.
    [Fact]
    public async Task WarnsWhenTheSequenceContinuesInWindowsWithoutALocalAdministrator()
    {
        Image image = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));
        SequenceDefinition definition = SequenceRequests.Minimal(image.Id);
        definition = definition with
        {
            Steps = [.. definition.Steps, new RunScriptStep { Id = Guid.NewGuid(), Name = "In Windows", Phase = SequencePhase.Windows, Script = "hostname" }],
        };

        SequenceValidation validation = await ValidateAsync(definition);

        Assert.Empty(validation.Problems);
        SequenceProblem warning = Assert.Single(validation.Warnings);
        Assert.Null(warning.StepId);
        Assert.StartsWith(
            "The sequence continues in Windows, but no Write answer file step adds the local administrator.",
            warning.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task AcceptsCultureNamesAndInputLocalesWindowsKnows()
    {
        Image image = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));
        SequenceDefinition definition = SequenceRequests.Minimal(image.Id);
        definition = definition with
        {
            Steps =
            [
                .. definition.Steps,
                new WriteUnattendStep
                {
                    Id = Guid.NewGuid(),
                    Name = "Answer file",
                    TimeZone = "W. Europe Standard Time",
                    Locale = "de-CH",
                    Keyboard = "de-CH; 0407:00000407; 0411:{03B5835F-F03C-411B-9CE2-AA23E1171E36}{A76C93D9-5523-4E90-AAFA-4DB112F9AC76}",
                },
            ],
        };

        SequenceValidation validation = await ValidateAsync(definition);

        Assert.Empty(validation.Problems);
        Assert.Empty(validation.Warnings);
    }

    [Fact]
    public async Task TheTemplateIsTheFlowBeforeTaskSequencesWithNewIdsEveryTime()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        Image image = await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096));

        IReadOnlyList<SequenceTemplate> templates = await ReadAsync<IReadOnlyList<SequenceTemplate>>(
            await administrator.GetAsync($"{SequenceRequests.Sequences}/templates?imageId={image.Id}"));
        SequenceTemplate template = templates[0];
        SequenceTemplate again = (await ReadAsync<IReadOnlyList<SequenceTemplate>>(
            await administrator.GetAsync($"{SequenceRequests.Sequences}/templates")))[0];

        Assert.Equal(["install-windows", "install-linux"], templates.Select(t => t.Key));
        Assert.Equal("install-windows", template.Key);
        Assert.Equal("Install Windows", template.Name);
        Assert.Collection(
            template.Definition.Steps,
            step => Assert.Equal(SequenceRequests.Json(new PartitionStep { Id = step.Id, Name = "Partition the disk" }), SequenceRequests.Json(step)),
            step => Assert.Equal(
                SequenceRequests.Json(new ApplyImageStep { Id = step.Id, Name = "Apply the image", ImageId = image.Id }),
                SequenceRequests.Json(step)),
            step => Assert.Equal(
                SequenceRequests.Json(new InjectDriversStep { Id = step.Id, Name = "Add the drivers for the model" }),
                SequenceRequests.Json(step)),
            step => Assert.Equal(
                SequenceRequests.Json(new WriteUnattendStep { Id = step.Id, Name = "Write the answer file" }),
                SequenceRequests.Json(step)));
        Assert.Empty(template.Definition.Steps.Select(s => s.Id).Intersect(again.Definition.Steps.Select(s => s.Id)));
        Assert.Equal(Guid.Empty, Assert.IsType<ApplyImageStep>(again.Definition.Steps[1]).ImageId);

        SequenceView created = await administrator.CreatedSequenceAsync(template.Definition);
        Assert.Empty(created.Problems);
        Assert.Empty(created.Warnings);
    }

    [Fact]
    public async Task DeletesASequence()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.Minimal(Guid.Empty));

        Assert.Equal(HttpStatusCode.NoContent, (await administrator.DeleteAsync($"{SequenceRequests.Sequences}/{sequence.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await administrator.GetAsync($"{SequenceRequests.Sequences}/{sequence.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await administrator.DeleteAsync($"{SequenceRequests.Sequences}/{sequence.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await administrator.SaveSequenceAsync(sequence)).StatusCode);

        AuditEvent deleted = (await AuditAsync(sequence.Id))[^1];
        Assert.Equal(AuditActions.SequenceDeleted, deleted.Action);
        Assert.Equal($"{sequence.Name}, revision 1.", deleted.Detail);
    }

    [Fact]
    public async Task PushesEverySaveAndTheDeletion()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using SignedInClient viewer = await application.SignInAsync(DdtRoleNames.Viewer);
        await using LiveListener live = await LiveListener.StartAsync(application, viewer);
        ChannelReader<SequenceChangedEvent> changes = live.Listen<SequenceChangedEvent>("sequenceChanged");

        SequenceView sequence = await administrator.CreatedSequenceAsync(SequenceRequests.Minimal(Guid.Empty));
        SequenceChangedEvent created = await LiveListener.NextAsync(changes, c => c.Id == sequence.Id);
        Assert.Equal(new SequenceChangedEvent(sequence.Id, 1, sequence.UpdatedBy), created);

        await ReadAsync<SequenceView>(await administrator.SaveSequenceAsync(sequence, name: $"Renamed {Guid.NewGuid():N}"));
        Assert.Equal(2, (await LiveListener.NextAsync(changes, c => c.Id == sequence.Id)).Revision);

        (await administrator.DeleteAsync($"{SequenceRequests.Sequences}/{sequence.Id}")).EnsureSuccessStatusCode();
        Assert.Null((await LiveListener.NextAsync(changes, c => c.Id == sequence.Id)).Revision);
    }
}
