// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;
using DDT.Server.Accounts;

namespace DDT.Server.Sequences;

// Checks the accounts a sequence's steps use. A stored account must exist and have a password this server can read.
// Every account must be allowed where the step sends it. The server checks this again when the step fetches the
// account, and only then for a share server made from a template. The validator refuses shares on a container, so
// only leaf steps are checked.
public static class SequenceAccountChecks
{
    public static IReadOnlyList<SequenceProblem> Check(SequenceDefinition definition, SequenceReferences references)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(references);

        List<SequenceProblem> problems = [];

        foreach (AccountSite site in AccountSites.Of(definition).Where(site => !site.Step.IsContainer))
        {
            if (Problem(site, definition, references) is { } message)
            {
                problems.Add(SequenceProblem.From(StepId(site.Step), site.Field, message));
            }
        }

        return problems;
    }

    private static Guid? StepId(SequenceStep step) => step.Id == Guid.Empty ? null : step.Id;

    // The validator checks that a reference names either a stored account or an Account input, and that the input
    // exists. This adds what only the server knows: the stored accounts and where they may be used.
    private static ServerMessage? Problem(AccountSite site, SequenceDefinition definition, SequenceReferences references)
    {
        if (site.Reference is not { } reference || (reference.AccountId is null) == (reference.Input is null))
        {
            return null;
        }

        // A server written literally in the path, not built from a value.
        string? host = site.Purpose == AccountPurpose.Share && AccountRules.WrittenHost(site.SharePath) is { } written
            && !written.Contains("{{", StringComparison.Ordinal)
                ? written
                : null;

        if (reference.AccountId is { } id)
        {
            if (!references.Accounts.TryGetValue(id, out AccountFacts? account))
            {
                return ServerMessages.SequenceAccountGone.With();
            }

            return account switch
            {
                { PasswordSet: false } => ServerMessages.SequenceAccountNoPassword.With("account", account.Name),
                { PasswordReadable: false } => ServerMessages.SequenceAccountPasswordUnreadable.With("account", account.Name),
                { Domain: null } when site.Purpose == AccountPurpose.Join => ServerMessages.SequenceAccountNoDomain.With("account", account.Name),
                { RunAs: false } when site.Purpose == AccountPurpose.RunAs => ServerMessages.SequenceAccountNoRunAs.With("account", account.Name),
                _ when host is not null && !AccountRules.Allows(account.Hosts, host) =>
                    ServerMessages.SequenceAccountHostNotAllowed.With("account", account.Name, "host", host),
                _ => null,
            };
        }

        string name = reference.Input!;
        InputDeclaration? input = definition.Inputs?.FirstOrDefault(i => i is not null && AccountRules.Same(i.Name, name));

        // The validator reports a missing input, or one of another kind, as accountInputUnknown.
        if (input is null || input.Kind != InputKind.Account)
        {
            return null;
        }

        AccountDestination destination = input.Account ?? new AccountDestination();

        return site.Purpose switch
        {
            AccountPurpose.Join when string.IsNullOrWhiteSpace(destination.Domain) => ServerMessages.SequenceAccountInputNoDomain.With("input", input.Name),
            AccountPurpose.RunAs when !destination.RunAs => ServerMessages.SequenceAccountInputNoRunAs.With("input", input.Name),
            AccountPurpose.Share when host is not null && !AccountRules.Allows((destination.Hosts ?? []).Select(h => h?.Trim() ?? ""), host) =>
                ServerMessages.SequenceAccountInputHostNotAllowed.With("input", input.Name, "host", host),
            _ => null,
        };
    }
}
