// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Sequences;
using DDT.Server.Accounts;
using DDT.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Deployments;

// The account a step names: a stored account as it is now, or the account given for the input, with the destination the
// input declared at that time. Nothing here logs a password.
public sealed class StepAccountLookup(DdtDbContext database, AccountProtector accounts, RunCredentials credentials)
{
    // The input is looked up in the run's own copy of the sequence.
    public async Task<(StepAccount? Account, string? Refusal)> ReadAsync(
        Deployment run,
        SequenceDefinition definition,
        AccountReference reference,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(reference);

        if ((reference.AccountId is null) == (reference.Input is null))
        {
            return (null, "The step names no single account: it needs a stored account or an account input.");
        }

        return reference.AccountId is { } id
            ? await StoredAsync(id, cancellationToken).ConfigureAwait(false)
            : await GivenAsync(run, definition, reference.Input ?? "", cancellationToken).ConfigureAwait(false);
    }

    private async Task<(StepAccount? Account, string? Refusal)> StoredAsync(Guid id, CancellationToken cancellationToken)
    {
        Account? stored = await database.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken).ConfigureAwait(false);

        if (stored is null)
        {
            return (null, "The account the step names is no longer there. Choose another account in the sequence and assign it again.");
        }

        if (stored.ProtectedPassword is null)
        {
            return (null, $"The account {stored.Name} has no password. Set it on the Accounts page.");
        }

        if (accounts.Unprotect(stored.Id, stored.ProtectedPassword) is not { } password)
        {
            return (null, $"The password of the account {stored.Name} no longer decrypts with this server's key ring. Enter it again on the Accounts page.");
        }

        return (new StepAccount(
            $"account {stored.Name} ({stored.Id:D})",
            stored.UserName,
            password,
            AccountRules.Trimmed(stored.Domain),
            AccountRules.ReadHosts(stored.Hosts),
            stored.RunAs), null);
    }

    private async Task<(StepAccount? Account, string? Refusal)> GivenAsync(
        Deployment run,
        SequenceDefinition definition,
        string name,
        CancellationToken cancellationToken)
    {
        InputDeclaration? input = definition.Inputs?.FirstOrDefault(i => i is { Kind: InputKind.Account } && AccountRules.Same(i.Name, name));

        if (input is null)
        {
            return (null, $"The run's sequence has no account input {name}.");
        }

        RunAccount? given = await credentials.ReadAsync(run.Id, input.Name, cancellationToken).ConfigureAwait(false);

        if (given is null)
        {
            return (null, $"No account was given for the input {input.Name} of this run.");
        }

        if (given.Password is null)
        {
            return (null, $"The account given for the input {input.Name} no longer decrypts with this server's key ring. Give it again.");
        }

        return (new StepAccount($"account given for the input {input.Name}", given.UserName, given.Password, given.Domain, given.Hosts, given.RunAs), null);
    }
}
