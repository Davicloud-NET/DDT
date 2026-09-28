// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using DDT.Contracts;
using DDT.Contracts.Accounts;
using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Contracts.Values;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Live;
using DDT.Server.Machines;
using DDT.Server.Sequences;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DeploymentStep = DDT.Server.Deployments.DeploymentStep;
using static DDT.Server.Tests.AccountRequests;
using static DDT.Server.Tests.TestReports;

namespace DDT.Server.Tests;

// The accounts a step uses reach its agent only while the step runs, only for the destination each account was entered
// for, and every read is audited. The configured domain is corp.example.
public sealed class RunStepAccountTests(DomainDeploymentApplication application) : IClassFixture<DomainDeploymentApplication>
{
    private const string GivenPassword = "Given \"at the machine\" <7>";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    // Partition and apply, then the steps given, as the Install Windows template begins.
    private async Task<(DeployingMachine Machine, AgentRun Run, SequenceView Sequence)> AssignedAsync(
        IReadOnlyList<InputDeclaration>? inputs,
        params SequenceStep[] steps)
    {
        SignedInClient administrator = await application.AdministratorAsync();
        DeployingMachine machine = await DeployingMachine.ApprovedAsync(application, administrator);
        Guid imageId = (await application.SeedImageAsync(RandomNumberGenerator.GetBytes(4096))).Id;
        SequenceView sequence = await administrator.CreatedSequenceAsync(
            SequenceRequests.Definition([.. SequenceRequests.Minimal(imageId).Steps, .. steps]) with { Inputs = inputs });

        Assert.Empty(sequence.Problems);
        await administrator.AssignedAsync(machine.Id, sequence.Id, "PC-0008");

        return (machine, (await machine.NextAsync()).Run!, sequence);
    }

    private static AgentRunReport Reached(AgentRun run, int running, SequencePhase phase = SequencePhase.WindowsPE) =>
        Report(
            DeploymentState.Running,
            [.. run.Sequence.Steps.Take(running + 1).Select((step, index) => Step(step, index < running ? StepState.Done : StepState.Running))],
            phase: phase);

    // The service in Windows, as it registers with the run token the agent in Windows PE handed over.
    private static async Task<string> ServiceTokenAsync(DeployingMachine machine) =>
        (await RegisteredMachine.ReadAsync<AgentRegistrationResult>(await machine.Agent.RegisterAsync(
            machine.Registration with { RunToken = machine.RunToken, Environment = AgentEnvironment.Windows }))).Token!;

    private static RunScriptStep Script(SequencePhase phase, AccountReference? runAs, params ShareConnection[] shares) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Copy drivers",
        Phase = phase,
        Interpreter = ScriptInterpreter.Cmd,
        Script = "echo copied",
        RunAs = runAs,
        Shares = shares.Length == 0 ? null : shares,
    };

    private static InputDeclaration AccountInput(string name, string? domain = null, bool runAs = false, params string[] hosts) => new()
    {
        Name = name,
        Label = name,
        Kind = InputKind.Account,
        AskAt = InputAsk.Machine,
        Account = new AccountDestination { Domain = domain, Hosts = hosts, RunAs = runAs },
    };

    private Task<List<string?>> SecretReadsAsync(Guid runId)
    {
        string subject = runId.ToString("D");

        return application.QueryAsync(database => database.AuditEvents
            .Where(e => e.SubjectId == subject && e.Action == AuditActions.DeploymentSecretRead)
            .OrderBy(e => e.Id)
            .Select(e => e.Detail)
            .ToListAsync(Cancellation));
    }

    private static async Task<AgentStepAccounts> AccountsAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);

        return (await response.Content.ReadFromJsonAsync<AgentStepAccounts>(TestJson.Options, Cancellation))!;
    }

    private static async Task<string?> RefusedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        return await TestDatabase.TitleAsync(response);
    }

    // The run's values as it started with them, and the variables its agent reported since.
    private Task SetValuesAsync(Guid runId, IReadOnlyList<ResolvedValue>? values, string? variables = null)
    {
        string? json = values is null ? null : JsonSerializer.Serialize(values, DdtJsonContext.Default.IReadOnlyListResolvedValue);

        return application.QueryAsync(database => database.Deployments
            .Where(d => d.Id == runId)
            .ExecuteUpdateAsync(d => d.SetProperty(x => x.Values, json).SetProperty(x => x.Variables, variables), Cancellation));
    }

    // As the runs keep the answer to an account input: from the declaration in the run's own copy of the sequence.
    private async Task<RunCredentialProblem?> GiveAsync(Guid runId, string input, string userName, string password)
    {
        using IServiceScope scope = application.Services.CreateScope();
        DdtDbContext database = scope.ServiceProvider.GetRequiredService<DdtDbContext>();
        RunCredentials credentials = scope.ServiceProvider.GetRequiredService<RunCredentials>();
        Deployment run = await database.Deployments.SingleAsync(d => d.Id == runId, Cancellation);
        SequenceDefinition snapshot = SequenceDocuments.Read(
            (await database.DeploymentSnapshots.AsNoTracking().SingleAsync(s => s.DeploymentId == runId, Cancellation)).Definition);

        RunCredentialProblem? problem = await credentials.KeepAsync(
            run,
            snapshot.Inputs!.Single(declared => declared.Name == input),
            new InputAnswer(input, null, userName, password),
            new RunCredentialGiver(null, "technician", AtMachine: true),
            Cancellation);

        await database.SaveChangesAsync(Cancellation);

        return problem;
    }

    private static ResolvedValue Value(string name, string value, bool overridden = false) =>
        new(name, value, overridden ? ValueSource.Role : ValueSource.Rule, Guid.NewGuid(), "Site", overridden);

    [Fact]
    public async Task AShareGoesOnlyToItsStepWhileItRuns()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        AccountView account = await administrator.CreatedAccountAsync(Request(name: $"Drivers {Guid.NewGuid():N}"));
        RunScriptStep script = Script(SequencePhase.WindowsPE, null, new ShareConnection(@"\\files.corp.example\drivers", new AccountReference(account.Id, null)));
        (DeployingMachine machine, AgentRun run, _) = await AssignedAsync(null, script);
        using DeployingMachine held = machine;

        Assert.StartsWith(
            "This machine has no such run that is running.",
            await RefusedAsync(await machine.Agent.RunAccountsAsync(machine.Id, machine.Token, run.Id, script.Id)),
            StringComparison.Ordinal);

        await machine.ReportOkAsync(run.Id, Reached(run, 1));
        Assert.StartsWith(
            "Only a step that is running",
            await RefusedAsync(await machine.Agent.RunAccountsAsync(machine.Id, machine.Token, run.Id, script.Id)),
            StringComparison.Ordinal);

        // A step that runs but connects nothing gets nothing.
        Assert.Equal("That step uses no account.", await RefusedAsync(await machine.Agent.RunAccountsAsync(machine.Id, machine.Token, run.Id, run.Sequence.Steps[1].Id)));

        await machine.ReportOkAsync(run.Id, Reached(run, 2));
        AgentStepAccounts accounts = await AccountsAsync(await machine.Agent.RunAccountsAsync(machine.Id, machine.Token, run.Id, script.Id));

        Assert.Null(accounts.RunAs);
        AgentShareConnection share = Assert.Single(accounts.Shares);
        Assert.Equal(@"\\files.corp.example\drivers", share.Path);
        Assert.Equal(@"CORP\svc-ddt", share.UserName);
        Assert.Equal(Password, share.Password);
        Assert.Equal(
            [$@"The account {account.Name} ({account.Id:D}) of step Copy drivers ({script.Id:D}) of {run.SequenceName}, for \\files.corp.example\drivers."],
            await SecretReadsAsync(run.Id));

        // Another machine's token gets nothing of this machine's run.
        using DeployingMachine other = await DeployingMachine.ApprovedAsync(application, administrator);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.Agent.RunAccountsAsync(machine.Id, other.Token, run.Id, script.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await other.Agent.RunAccountsAsync(other.Id, other.Token, run.Id, script.Id)).StatusCode);

        // Over with the run: the step is done, and so is the token of a machine that ends its run in Windows PE.
        await machine.ReportOkAsync(run.Id, Report(DeploymentState.Done, [.. run.Sequence.Steps.Select(s => Step(s, StepState.Done))]));
        Assert.Equal(HttpStatusCode.Unauthorized, (await machine.Agent.RunAccountsAsync(machine.Id, machine.Token, run.Id, script.Id)).StatusCode);
        Assert.Single(await SecretReadsAsync(run.Id));
    }

    // A share's server is made from the values the run started with, never from a variable a step reported since.
    [Fact]
    public async Task ASharePathIsWorkedOutFromTheValuesTheRunStartedWith()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        AccountView account = await administrator.CreatedAccountAsync(Request(hosts: ["files.corp.example"]));
        RunScriptStep script = Script(
            SequencePhase.WindowsPE,
            null,
            new ShareConnection(@"\\{{FileServer|lower}}\drivers\{{Site}}", new AccountReference(account.Id, null)));
        (DeployingMachine machine, AgentRun run, _) = await AssignedAsync(null, script);
        using DeployingMachine held = machine;

        await machine.ReportOkAsync(run.Id, Reached(run, 2));
        await SetValuesAsync(
            run.Id,
            [Value("FileServer", "FILES.corp.example"), Value("FileServer", "evil.example", overridden: true), Value("Site", "vienna")],
            """{"FileServer":"evil.example","Site":"elsewhere"}""");

        AgentStepAccounts accounts = await AccountsAsync(await machine.Agent.RunAccountsAsync(machine.Id, machine.Token, run.Id, script.Id));
        Assert.Equal(@"\\files.corp.example\drivers\vienna", Assert.Single(accounts.Shares).Path);

        // A server the account does not name is refused, whichever value names it.
        await SetValuesAsync(run.Id, [Value("FileServer", "evil.example"), Value("Site", "vienna")]);
        Assert.Equal(
            $"The account {account.Name} ({account.Id:D}) may not connect to evil.example, which the share 1 of Copy drivers names.",
            await RefusedAsync(await machine.Agent.RunAccountsAsync(machine.Id, machine.Token, run.Id, script.Id)));

        // A value the run did not start with does not count, however the agent reported it.
        await SetValuesAsync(run.Id, [Value("Site", "vienna")], """{"FileServer":"files.corp.example"}""");
        Assert.StartsWith(
            @"The path \\{{FileServer|lower}}\drivers\{{Site}} of share 1 of Copy drivers cannot be worked out from the values the run started with.",
            await RefusedAsync(await machine.Agent.RunAccountsAsync(machine.Id, machine.Token, run.Id, script.Id)),
            StringComparison.Ordinal);

        // A value that walks up out of the share is no share path.
        await SetValuesAsync(run.Id, [Value("FileServer", "files.corp.example"), Value("Site", "..")]);
        Assert.EndsWith(
            @"is not a share such as \\server\share.",
            await RefusedAsync(await machine.Agent.RunAccountsAsync(machine.Id, machine.Token, run.Id, script.Id)),
            StringComparison.Ordinal);

        Assert.Single(await SecretReadsAsync(run.Id));
    }

    [Fact]
    public async Task AScriptRunsAsAnAccountOnlyInTheServiceInWindows()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        AccountView account = await administrator.CreatedAccountAsync(Request(userName: "svc-tools@corp.example", hosts: [], runAs: true));
        RunScriptStep script = Script(SequencePhase.Windows, new AccountReference(account.Id, null));
        (DeployingMachine machine, AgentRun run, _) = await AssignedAsync(null, script);
        using DeployingMachine held = machine;

        await machine.ReportOkAsync(run.Id, Reached(run, 2, SequencePhase.Windows));
        Assert.Equal(
            "A script runs as an account in Windows, and this agent registered from Windows PE.",
            await RefusedAsync(await machine.Agent.RunAccountsAsync(machine.Id, machine.Token, run.Id, script.Id)));

        string service = await ServiceTokenAsync(machine);
        AgentStepAccounts accounts = await AccountsAsync(await machine.Agent.RunAccountsAsync(machine.Id, service, run.Id, script.Id));

        Assert.Equal("svc-tools@corp.example", accounts.RunAs?.UserName);
        Assert.Equal(Password, accounts.RunAs?.Password);
        Assert.Empty(accounts.Shares);
        Assert.Equal(
            [$"The account {account.Name} ({account.Id:D}) of step Copy drivers ({script.Id:D}) of {run.SequenceName}, to run the script as svc-tools@corp.example."],
            await SecretReadsAsync(run.Id));

        // Taking the right away takes effect at the next fetch.
        await application.QueryAsync(database => database.Accounts
            .Where(a => a.Id == account.Id)
            .ExecuteUpdateAsync(a => a.SetProperty(x => x.RunAs, false), Cancellation));
        Assert.Equal(
            $"The account {account.Name} ({account.Id:D}) does not let scripts run as it.",
            await RefusedAsync(await machine.Agent.RunAccountsAsync(machine.Id, service, run.Id, script.Id)));
        Assert.Single(await SecretReadsAsync(run.Id));
    }

    // The run keeps its own copy of the sequence, so an edit after the assignment sends no account anywhere else, and
    // the account given for the run goes only where its input said when it was given.
    [Fact]
    public async Task AnAccountGivenForTheRunGoesOnlyWhereItsInputSaid()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        RunScriptStep script = Script(
            SequencePhase.WindowsPE,
            null,
            new ShareConnection(@"\\files.corp.example\drivers", new AccountReference(null, "ShareAccount")),
            new ShareConnection(@"\\{{Backup}}\drivers", new AccountReference(null, "shareaccount")));
        (DeployingMachine machine, AgentRun run, SequenceView sequence) = await AssignedAsync(
            [AccountInput("ShareAccount", hosts: ["files.corp.example", "backup.corp.example"])],
            script);
        using DeployingMachine held = machine;

        await machine.ReportOkAsync(run.Id, Reached(run, 2));
        await SetValuesAsync(run.Id, [Value("Backup", "backup.corp.example")]);
        Assert.Equal(
            "No account was given for the input ShareAccount of this run.",
            await RefusedAsync(await machine.Agent.RunAccountsAsync(machine.Id, machine.Token, run.Id, script.Id)));

        Assert.Equal("userName", (await GiveAsync(run.Id, "ShareAccount", "jane", GivenPassword))?.Field);
        Assert.Equal("password", (await GiveAsync(run.Id, "ShareAccount", @"CORP\jane", ""))?.Field);
        Assert.Null(await GiveAsync(run.Id, "ShareAccount", @"CORP\someone", "Replaced 1"));
        Assert.Null(await GiveAsync(run.Id, "ShareAccount", @" CORP\jane ", GivenPassword));

        // An edit of the sequence since then points elsewhere, which the run never sees.
        SequenceDefinition edited = sequence.Definition with
        {
            Inputs = [AccountInput("ShareAccount", hosts: ["evil.example"])],
            Steps =
            [
                .. sequence.Definition.Steps.Take(2),
                script with { Shares = [new ShareConnection(@"\\evil.example\loot", new AccountReference(null, "ShareAccount"))] },
            ],
        };
        (await administrator.SaveSequenceAsync(sequence, edited)).EnsureSuccessStatusCode();

        AgentStepAccounts accounts = await AccountsAsync(await machine.Agent.RunAccountsAsync(machine.Id, machine.Token, run.Id, script.Id));

        Assert.Equal([@"\\files.corp.example\drivers", @"\\backup.corp.example\drivers"], accounts.Shares.Select(s => s.Path));
        Assert.All(accounts.Shares, s => Assert.Equal((@"CORP\jane", GivenPassword), (s.UserName, s.Password)));
        Assert.Equal(
            [
                $@"The account given for the input ShareAccount of step Copy drivers ({script.Id:D}) of {run.SequenceName}, for \\files.corp.example\drivers.",
                $@"The account given for the input ShareAccount of step Copy drivers ({script.Id:D}) of {run.SequenceName}, for \\backup.corp.example\drivers.",
            ],
            await SecretReadsAsync(run.Id));

        // One credential for the input, kept with the destination the input declared, and read back whole.
        RunCredential stored = await application.QueryAsync(database => database.RunCredentials.AsNoTracking().SingleAsync(c => c.DeploymentId == run.Id, Cancellation));
        using IServiceScope scope = application.Services.CreateScope();
        RunAccount? given = await scope.ServiceProvider.GetRequiredService<RunCredentials>().ReadAsync(run.Id, "SHAREACCOUNT", Cancellation);

        Assert.Equal("""["files.corp.example","backup.corp.example"]""", stored.Hosts);
        Assert.True(stored.ProvidedAtMachine);
        Assert.Equal("technician", stored.ProvidedByName);
        Assert.DoesNotContain(GivenPassword, stored.ProtectedPassword, StringComparison.Ordinal);
        Assert.Equal(GivenPassword, given?.Password);
        Assert.DoesNotContain(GivenPassword, given!.ToString(), StringComparison.Ordinal);

        // The credential's own servers bind it, whatever value names another.
        await SetValuesAsync(run.Id, [Value("Backup", "archive.corp.example")]);
        Assert.StartsWith(
            "The account given for the input ShareAccount may not connect to archive.corp.example",
            await RefusedAsync(await machine.Agent.RunAccountsAsync(machine.Id, machine.Token, run.Id, script.Id)),
            StringComparison.Ordinal);
    }

    // Only a leaf step connects shares: a step in a group gets its own and never the group's, and the group gets nothing,
    // whatever a document from outside gave it.
    [Fact]
    public async Task AStepInAGroupConnectsOnlyItsOwnShares()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        AccountView account = await administrator.CreatedAccountAsync(Request(hosts: ["files.corp.example", "tools.corp.example"]));
        AccountReference named = new(account.Id, null);
        RunScriptStep script = Script(SequencePhase.WindowsPE, null, new ShareConnection(@"\\tools.corp.example\bin", named));
        GroupStep group = new()
        {
            Id = Guid.NewGuid(),
            Name = "Drivers",
            Shares = [new ShareConnection(@"\\files.corp.example\drivers", named)],
            Steps = [script],
        };
        SequenceDefinition definition = SequenceRequests.Definition(group);

        (AgentStepAccounts? accounts, string? refusal) = await StepAccountsFromSnapshotAsync(definition, script.Id);

        Assert.Null(refusal);
        Assert.Equal([@"\\tools.corp.example\bin"], accounts!.Shares.Select(s => s.Path));

        (_, string? container) = await StepAccountsFromSnapshotAsync(definition, group.Id);
        Assert.Equal("A group, an IF or a Repeat uses no account itself. Only the steps in it do.", container);
    }

    [Fact]
    public async Task AJoinWithAStoredAccountJoinsThatAccountsDomain()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        AccountView lab = await administrator.CreatedAccountAsync(Request(userName: @"LAB\joiner", domain: "lab.example", hosts: []));
        AccountView corp = await administrator.CreatedAccountAsync(Request(userName: @"CORP\kiosk-join", domain: "CORP.example", hosts: []));
        JoinDomainStep joinLab = new() { Id = Guid.NewGuid(), Name = "Join", Account = new AccountReference(lab.Id, null) };
        JoinDomainStep joinCorp = new() { Id = Guid.NewGuid(), Name = "Join", Account = new AccountReference(corp.Id, null) };

        (DeployingMachine machine, AgentRun run, _) = await AssignedAsync(null, joinLab);
        using DeployingMachine held = machine;
        await machine.ReportOkAsync(run.Id, Reached(run, 2, SequencePhase.Windows));
        string service = await ServiceTokenAsync(machine);

        HttpResponseMessage response = await machine.Agent.RunCredentialsAsync(machine.Id, service, run.Id, joinLab.Id);
        Assert.True(response.Headers.CacheControl?.NoStore);
        AgentJoinDomainCredentials credentials = (await RegisteredMachine.ReadAsync<AgentJoinDomainCredentials>(response))!;

        // The configured organizational unit is corp.example's, so it is not for lab.example.
        Assert.Equal(("lab.example", null, @"LAB\joiner", Password), (credentials.Domain, credentials.OrganizationalUnit, credentials.UserName, credentials.Password));
        Assert.Equal(
            [$"The domain join credentials of step Join ({joinLab.Id:D}) of {run.SequenceName}, from the account {lab.Name} ({lab.Id:D}), for lab.example."],
            await SecretReadsAsync(run.Id));

        // An account without its domain joins nothing.
        await application.QueryAsync(database => database.Accounts
            .Where(a => a.Id == lab.Id)
            .ExecuteUpdateAsync(a => a.SetProperty(x => x.Domain, (string?)null), Cancellation));
        Assert.Equal(
            $"The account {lab.Name} ({lab.Id:D}) names no domain, so it joins none.",
            await RefusedAsync(await machine.Agent.RunCredentialsAsync(machine.Id, service, run.Id, joinLab.Id)));

        (DeployingMachine second, AgentRun secondRun, _) = await AssignedAsync(null, joinCorp);
        using DeployingMachine heldToo = second;
        await second.ReportOkAsync(secondRun.Id, Reached(secondRun, 2, SequencePhase.Windows));
        AgentJoinDomainCredentials corpCredentials = await RegisteredMachine.ReadAsync<AgentJoinDomainCredentials>(
            await second.Agent.RunCredentialsAsync(second.Id, await ServiceTokenAsync(second), secondRun.Id, joinCorp.Id));

        Assert.Equal(("CORP.example", "OU=Workstations,DC=corp,DC=example", @"CORP\kiosk-join"), (corpCredentials.Domain, corpCredentials.OrganizationalUnit, corpCredentials.UserName));
    }

    [Fact]
    public async Task AJoinWithAnAccountInputJoinsTheDomainItsInputDeclared()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        JoinDomainStep join = new()
        {
            Id = Guid.NewGuid(),
            Name = "Join",
            OrganizationalUnit = "OU=Lab,DC=lab,DC=example",
            Account = new AccountReference(null, "JoinAccount"),
        };
        (DeployingMachine machine, AgentRun run, SequenceView sequence) = await AssignedAsync([AccountInput("JoinAccount", "lab.example")], join);
        using DeployingMachine held = machine;

        Assert.Null(await GiveAsync(run.Id, "JoinAccount", @"LAB\jane", GivenPassword));

        // The domain was the input's when the account was given; the sequence's edits since do not reach the run.
        (await administrator.SaveSequenceAsync(sequence, sequence.Definition with { Inputs = [AccountInput("JoinAccount", "evil.example")] }))
            .EnsureSuccessStatusCode();

        await machine.ReportOkAsync(run.Id, Reached(run, 2, SequencePhase.Windows));
        AgentJoinDomainCredentials credentials = await RegisteredMachine.ReadAsync<AgentJoinDomainCredentials>(
            await machine.Agent.RunCredentialsAsync(machine.Id, await ServiceTokenAsync(machine), run.Id, join.Id));

        Assert.Equal(("lab.example", "OU=Lab,DC=lab,DC=example", @"LAB\jane", GivenPassword), (credentials.Domain, credentials.OrganizationalUnit, credentials.UserName, credentials.Password));
        Assert.Equal(
            [$"The domain join credentials of step Join ({join.Id:D}) of {run.SequenceName}, from the account given for the input JoinAccount, for lab.example."],
            await SecretReadsAsync(run.Id));
    }

    // No password reaches the log, an audit row or the hub at any point, neither a stored account's nor one given for a
    // run, nor the join account of the settings.
    [Fact]
    public async Task NoPasswordReachesTheLogTheAuditOrTheHub()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        await using LiveListener listener = await LiveListener.StartAsync(application, administrator);
        string[] events = [LiveEvents.AccountChanged, LiveEvents.AuditAppended, LiveEvents.MachineChanged, LiveEvents.RunChanged, LiveEvents.SequenceChanged];
        ChannelReader<JsonElement>[] readers = [.. events.Select(listener.Listen<JsonElement>)];

        AccountView account = await administrator.CreatedAccountAsync(Request(name: $"Everything {Guid.NewGuid():N}", domain: "lab.example", runAs: true));
        AccountReference named = new(account.Id, null);
        RunScriptStep script = Script(
            SequencePhase.Windows,
            named,
            new ShareConnection(@"\\files.corp.example\drivers", named),
            new ShareConnection(@"\\files.corp.example\given", new AccountReference(null, "Given")));
        JoinDomainStep join = new() { Id = Guid.NewGuid(), Name = "Join", Account = new AccountReference(null, "Given") };
        (DeployingMachine machine, AgentRun run, _) = await AssignedAsync(
            [AccountInput("Given", "lab.example", false, "files.corp.example")],
            script,
            join);
        using DeployingMachine held = machine;

        Assert.Null(await GiveAsync(run.Id, "Given", @"LAB\jane", GivenPassword));
        await machine.ReportOkAsync(run.Id, Reached(run, 2, SequencePhase.Windows));
        string service = await ServiceTokenAsync(machine);
        await AccountsAsync(await machine.Agent.RunAccountsAsync(machine.Id, service, run.Id, script.Id));
        await machine.ReportOkAsync(run.Id, Reached(run, 3, SequencePhase.Windows));
        (await machine.Agent.RunCredentialsAsync(machine.Id, service, run.Id, join.Id)).EnsureSuccessStatusCode();
        await machine.ReportOkAsync(run.Id, Report(DeploymentState.Done, [.. run.Sequence.Steps.Select(s => Step(s, StepState.Done))], phase: SequencePhase.Windows));

        // The run is over, and the account given for it is gone with it.
        Assert.False(await application.QueryAsync(database => database.RunCredentials.AnyAsync(c => c.DeploymentId == run.Id, Cancellation)));

        string[] secrets = [Password, GivenPassword, DomainDeploymentApplication.JoinPassword];

        // The pushes of the reads have gone out once the one that says the run is done has.
        await LiveListener.NextAsync(readers[Array.IndexOf(events, LiveEvents.RunChanged)], e => e.GetProperty("run").GetProperty("state").GetString() == "Done");
        List<string> pushed = [];

        foreach (ChannelReader<JsonElement> reader in readers)
        {
            while (reader.TryRead(out JsonElement payload))
            {
                pushed.Add(payload.GetRawText());
            }
        }

        List<AuditEvent> audit = await application.QueryAsync(database => database.AuditEvents.AsNoTracking().ToListAsync(Cancellation));

        Assert.Equal(4, (await SecretReadsAsync(run.Id)).Count);
        Assert.Contains(pushed, payload => payload.Contains(AuditActions.DeploymentSecretRead, StringComparison.Ordinal));
        Assert.All(secrets, secret =>
        {
            Assert.All(application.Log.Entries, entry =>
            {
                Assert.DoesNotContain(secret, entry.Message, StringComparison.Ordinal);
                Assert.DoesNotContain(secret, entry.Exception ?? string.Empty, StringComparison.Ordinal);
            });
            Assert.All(audit, row => Assert.DoesNotContain(secret, row.Detail ?? string.Empty, StringComparison.Ordinal));
            Assert.All(pushed, payload => Assert.DoesNotContain(secret, payload, StringComparison.Ordinal));
        });
    }

    // A tree that the runs of this build cannot yet make, read the way a run does: its step running in its copy.
    private async Task<(AgentStepAccounts? Accounts, string? Refusal)> StepAccountsFromSnapshotAsync(SequenceDefinition definition, Guid stepId)
    {
        (DeployingMachine machine, AgentRun run, _) = await AssignedAsync(null);
        using DeployingMachine held = machine;

        await machine.ReportOkAsync(run.Id, Reached(run, 1));

        string written = SequenceDocuments.Write(definition with { Steps = [.. run.Sequence.Steps, .. definition.Steps] });
        await application.QueryAsync(async database =>
        {
            await database.DeploymentSnapshots
                .Where(s => s.DeploymentId == run.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Definition, written), Cancellation);
            database.DeploymentSteps.Add(new DeploymentStep
            {
                DeploymentId = run.Id,
                StepId = stepId,
                Index = 99,
                Name = "Inside",
                Kind = "runScript",
                State = StepState.Running,
            });

            return await database.SaveChangesAsync(Cancellation);
        });

        using IServiceScope scope = application.Services.CreateScope();
        Machine stored = await scope.ServiceProvider.GetRequiredService<DdtDbContext>().Machines.AsNoTracking().SingleAsync(m => m.Id == machine.Id, Cancellation);

        return await scope.ServiceProvider.GetRequiredService<RunSecrets>().StepAccountsAsync(stored, run.Id, stepId, null, Cancellation);
    }
}
