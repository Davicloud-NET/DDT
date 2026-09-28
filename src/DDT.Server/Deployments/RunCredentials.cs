// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Sequences;
using DDT.Server.Accounts;
using DDT.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Deployments;

// The accounts given for a run's Account inputs. Each is encrypted for that run and input only, and read back just in
// time for the step that uses it. Nothing here logs or audits a password. The caller audits the answer by input name.
public sealed class RunCredentials(DdtDbContext database, RunCredentialProtector protector, TimeProvider timeProvider)
{
    // Only tracks the change. The caller saves it with the answer, so both are stored or neither. Input must come from
    // the run's own snapshot, never the current sequence, because its destination decides where the password may go.
    public async Task<RunCredentialProblem?> KeepAsync(
        Deployment run,
        InputDeclaration input,
        InputAnswer answer,
        RunCredentialGiver giver,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(answer);
        ArgumentNullException.ThrowIfNull(giver);

        if (input.Kind != InputKind.Account || string.IsNullOrEmpty(input.Name) || input.Name.Length > RunCredential.MaxInputNameLength)
        {
            throw new ArgumentException("Only an account input of the run's sequence takes an account.", nameof(input));
        }

        if (!string.Equals(answer.Name, input.Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The answer is to another input.", nameof(answer));
        }

        // RunCredentialCleanup would delete it in this same save.
        if (run.State is not (DeploymentState.Assigned or DeploymentState.Running))
        {
            throw new InvalidOperationException("The run is over, so it takes no account.");
        }

        if (AccountRules.UserNameProblem(answer.UserName) is { } userName)
        {
            return new RunCredentialProblem("userName", userName);
        }

        if (AccountRules.PasswordProblem(answer.Password) is { } password)
        {
            return new RunCredentialProblem("password", password);
        }

        AccountDestination destination = input.Account ?? new AccountDestination();
        RunCredential? credential = await database.RunCredentials.FindAsync([run.Id, input.Name], cancellationToken).ConfigureAwait(false);

        if (credential is null)
        {
            credential = new RunCredential { DeploymentId = run.Id, InputName = input.Name, UserName = "", ProtectedPassword = "" };
            database.RunCredentials.Add(credential);
        }

        credential.UserName = answer.UserName!.Trim();
        credential.ProtectedPassword = protector.Protect(run.Id, input.Name, answer.Password!);
        credential.Domain = AccountRules.Trimmed(destination.Domain);
        credential.Hosts = AccountRules.WriteHosts([.. (destination.Hosts ?? []).Select(AccountRules.Trimmed).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)]);
        credential.RunAs = destination.RunAs;
        credential.ProvidedByUserId = giver.UserId;
        credential.ProvidedByName = StoredText.Bound(giver.Name, 256);
        credential.ProvidedAtMachine = giver.AtMachine;
        credential.CreatedUtc = timeProvider.GetUtcNow();

        return null;
    }

    // Null if no account was given for the input. Its Password is null if this server's key ring can't decrypt it.
    public async Task<RunAccount?> ReadAsync(Guid runId, string inputName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputName);

        RunCredential? credential = (await database.RunCredentials
            .AsNoTracking()
            .Where(c => c.DeploymentId == runId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .FirstOrDefault(c => string.Equals(c.InputName, inputName, StringComparison.OrdinalIgnoreCase));

        return credential is null
            ? null
            : new RunAccount(
                credential.InputName,
                credential.UserName,
                protector.Unprotect(runId, credential.InputName, credential.ProtectedPassword),
                credential.Domain,
                AccountRules.ReadHosts(credential.Hosts),
                credential.RunAs,
                credential.ProvidedByName,
                credential.ProvidedAtMachine,
                credential.CreatedUtc);
    }

    // The run's account inputs that have an answer, ignoring case like input names do.
    public async Task<IReadOnlySet<string>> AnsweredAsync(Guid runId, CancellationToken cancellationToken)
    {
        List<string> names = await database.RunCredentials
            .AsNoTracking()
            .Where(c => c.DeploymentId == runId)
            .Select(c => c.InputName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return names.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
