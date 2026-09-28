// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Accounts;
using DDT.Contracts.Sequences;
using DDT.Server.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DDT.Server.Tests.AccountRequests;

namespace DDT.Server.Tests;

// A sequence that names an account runs only when the account exists, has a password the server can read, and may go
// where the step sends it: the server would refuse the step otherwise, after the disk was erased. No domain is
// configured here, so a join without an account has a problem of its own.
public sealed class SequenceAccountCheckTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private async Task<SequenceValidation> ValidateAsync(SequenceDefinition definition) =>
        await RegisteredMachine.ReadAsync<SequenceValidation>(
            await (await application.AdministratorAsync()).PostAsync($"{SequenceRequests.Sequences}/validate", definition));

    private async Task<AccountView> AccountAsync(SaveAccountRequest request) =>
        await (await application.AdministratorAsync()).CreatedAccountAsync(request);

    private static RunScriptStep Script(AccountReference? runAs = null, params ShareConnection[] shares) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Copy tools",
        Phase = SequencePhase.Windows,
        Interpreter = ScriptInterpreter.Cmd,
        Script = "echo copied",
        RunAs = runAs,
        Shares = shares.Length == 0 ? null : shares,
    };

    private static JoinDomainStep Join(AccountReference? account) => new() { Id = Guid.NewGuid(), Name = "Join", Account = account };

    private static InputDeclaration AccountInput(string name, string? domain = null, bool runAs = false, params string[] hosts) => new()
    {
        Name = name,
        Label = name,
        Kind = InputKind.Account,
        Account = new AccountDestination { Domain = domain, Hosts = hosts, RunAs = runAs },
    };

    // The step's problems with its accounts; the validator has others to say about these steps.
    private static List<(string? Field, string? Code)> AccountProblems(SequenceValidation validation, SequenceStep step) =>
    [
        .. validation.Problems
            .Where(problem => problem.StepId == step.Id && problem.Code?.StartsWith("sequence.account", StringComparison.Ordinal) == true)
            .Select(problem => (problem.Field, problem.Code)),
    ];

    [Fact]
    public async Task AStoredAccountMustExistAndHaveAPassword()
    {
        AccountView without = await AccountAsync(Request(runAs: true, password: null));
        RunScriptStep gone = Script(new AccountReference(Guid.NewGuid(), null));
        RunScriptStep empty = Script(new AccountReference(without.Id, null));
        RunScriptStep both = Script(new AccountReference(without.Id, "Account"));
        RunScriptStep neither = Script(new AccountReference(null, null));

        SequenceValidation validation = await ValidateAsync(SequenceRequests.Definition(gone, empty, both, neither));

        Assert.Equal([("runAs", "sequence.accountGone")], AccountProblems(validation, gone));
        Assert.Equal([("runAs", "sequence.accountNoPassword")], AccountProblems(validation, empty));
        Assert.Equal([("runAs", "sequence.accountChooseOne")], AccountProblems(validation, both));
        Assert.Equal([("runAs", "sequence.accountChooseOne")], AccountProblems(validation, neither));
        Assert.Contains(without.Name, validation.Problems.Single(problem => problem.Code == "sequence.accountNoPassword").Message, StringComparison.Ordinal);
    }

    // Moving the key ring leaves a password that no longer decrypts, which the step could not be given.
    [Fact]
    public async Task AStoredPasswordMustDecrypt()
    {
        AccountView account = await AccountAsync(Request(runAs: true));
        string foreign = application.Services.GetRequiredService<AccountProtector>().Protect(Guid.NewGuid(), Password);
        RunScriptStep script = Script(new AccountReference(account.Id, null));

        await application.QueryAsync(database => database.Accounts
            .Where(a => a.Id == account.Id)
            .ExecuteUpdateAsync(a => a.SetProperty(x => x.ProtectedPassword, foreign), TestContext.Current.CancellationToken));

        Assert.Equal(
            [("runAs", "sequence.accountPasswordUnreadable")],
            AccountProblems(await ValidateAsync(SequenceRequests.Definition(script)), script));
    }

    [Fact]
    public async Task AnAccountGoesOnlyWhereItMay()
    {
        AccountView account = await AccountAsync(Request(domain: null, hosts: ["files.corp.example"], runAs: false));
        AccountReference named = new(account.Id, null);
        RunScriptStep runAs = Script(named);
        JoinDomainStep join = Join(named);
        RunScriptStep shares = Script(
            null,
            new ShareConnection(@"\\FILES.corp.example\tools", named),
            new ShareConnection(@"\\evil.example\loot", named),
            new ShareConnection(@"\\{{FileServer}}\tools", named));

        SequenceValidation validation = await ValidateAsync(SequenceRequests.Definition(runAs, join, shares));

        Assert.Equal([("runAs", "sequence.accountNoRunAs")], AccountProblems(validation, runAs));
        Assert.Equal([("account", "sequence.accountNoDomain")], AccountProblems(validation, join));

        // A server made from a value is checked when the run fetches the share, with the values the run started with.
        Assert.Equal([("shares[1].account", "sequence.accountHostNotAllowed")], AccountProblems(validation, shares));
        Assert.Contains("evil.example", validation.Problems.Single(problem => problem.Code == "sequence.accountHostNotAllowed").Message, StringComparison.Ordinal);
    }

    // The configured domain is not the account's, so a join with an account needs none.
    [Fact]
    public async Task AJoinWithAnAccountNeedsNoConfiguredDomain()
    {
        AccountView account = await AccountAsync(Request(domain: "lab.example", runAs: true));
        JoinDomainStep withAccount = Join(new AccountReference(account.Id, null));
        JoinDomainStep withoutAccount = Join(null);

        Assert.Empty(AccountProblems(await ValidateAsync(SequenceRequests.Definition(withAccount)), withAccount));
        Assert.DoesNotContain((await ValidateAsync(SequenceRequests.Definition(withAccount))).Problems, problem => problem.Code == "sequence.noDomainSet");
        Assert.Contains(
            (await ValidateAsync(SequenceRequests.Definition(withoutAccount))).Problems,
            problem => problem.StepId == withoutAccount.Id && problem.Code == "sequence.noDomainSet");
    }

    [Fact]
    public async Task AnAccountInputDeclaresWhereItsAccountMayGo()
    {
        RunScriptStep missing = Script(new AccountReference(null, "Nobody"));
        RunScriptStep notAccount = Script(new AccountReference(null, "Site"));
        RunScriptStep runAs = Script(new AccountReference(null, "Operator"));
        JoinDomainStep join = Join(new AccountReference(null, "operator"));
        RunScriptStep shares = Script(
            null,
            new ShareConnection(@"\\files.corp.example\tools", new AccountReference(null, "Operator")),
            new ShareConnection(@"\\backup.corp.example\tools", new AccountReference(null, "Operator")));
        RunScriptStep allowed = Script(
            new AccountReference(null, "Joiner"),
            new ShareConnection(@"\\files.corp.example\tools", new AccountReference(null, "Joiner")));
        JoinDomainStep joins = Join(new AccountReference(null, "Joiner"));

        SequenceDefinition definition = SequenceRequests.Definition(missing, notAccount, runAs, join, shares, allowed, joins) with
        {
            Inputs =
            [
                new InputDeclaration { Name = "Site", Label = "Site", Kind = InputKind.Text },
                AccountInput("Operator", hosts: "files.corp.example"),
                AccountInput("Joiner", "corp.example", true, "files.corp.example"),
            ],
        };

        SequenceValidation validation = await ValidateAsync(definition);

        Assert.Equal([("runAs", "sequence.accountInputMissing")], AccountProblems(validation, missing));
        Assert.Equal([("runAs", "sequence.accountInputNotAccount")], AccountProblems(validation, notAccount));
        Assert.Equal([("runAs", "sequence.accountInputNoRunAs")], AccountProblems(validation, runAs));
        Assert.Equal([("account", "sequence.accountInputNoDomain")], AccountProblems(validation, join));
        Assert.Equal([("shares[1].account", "sequence.accountInputHostNotAllowed")], AccountProblems(validation, shares));
        Assert.Empty(AccountProblems(validation, allowed));
        Assert.Empty(AccountProblems(validation, joins));
    }

    // Anywhere in the tree. Only a leaf step connects shares, so a container's are a problem whatever account they name.
    [Fact]
    public async Task StepsInsideContainersAreCheckedAndContainersConnectNoShares()
    {
        AccountView account = await AccountAsync(Request(hosts: ["files.corp.example"]));
        ShareConnection allowed = new(@"\\files.corp.example\tools", new AccountReference(account.Id, null));
        RunScriptStep inner = Script(null, new ShareConnection(@"\\evil.example\loot", new AccountReference(account.Id, null)));
        GroupStep group = new() { Id = Guid.NewGuid(), Name = "Tools", Shares = [allowed], Steps = [inner] };
        IfStep test = new()
        {
            Id = Guid.NewGuid(),
            Name = "Laptops",
            Test = new TestCondition("Model", ConditionOperator.Contains, "Latitude"),
            Shares = [new ShareConnection(@"\\evil.example\loot", new AccountReference(Guid.NewGuid(), null))],
        };
        RepeatStep repeat = new()
        {
            Id = Guid.NewGuid(),
            Name = "Retry",
            Until = new TestCondition("LastStepFailed", ConditionOperator.Equals, "false"),
            Shares = [allowed],
        };

        SequenceValidation validation = await ValidateAsync(SequenceRequests.Definition(group, test, repeat));

        Assert.Equal([("shares[0].account", "sequence.accountHostNotAllowed")], AccountProblems(validation, inner));

        foreach ((SequenceStep container, string kind) in new (SequenceStep, string)[] { (group, "A group"), (test, "An IF"), (repeat, "A repeat") })
        {
            SequenceProblem problem = Assert.Single(validation.Problems, problem => problem.StepId == container.Id && problem.Code == "sequence.containerShares");

            Assert.Equal("shares", problem.Field);
            Assert.StartsWith($"{kind} connects no shares", problem.Message, StringComparison.Ordinal);
            Assert.Empty(AccountProblems(validation, container));
        }
    }
}
