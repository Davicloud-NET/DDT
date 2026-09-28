// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Contracts.Messages;
using DDT.Contracts.Sequences;
using DDT.Server.Accounts;
using DDT.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Deployments;

// The accounts given for one run, in answer to the Account inputs of its sequence, on the web or at the machine. Each is
// kept encrypted for that run and input only, with the destination its input declared at that moment, and read back
// just in time for the step that uses it (RunSecrets). RunCredentialCleanup deletes them in the save that ends the run.
// Nothing here logs or audits a password; the caller audits the answer, naming the input.
public sealed class RunCredentials(DdtDbContext database, RunCredentialProtector protector, TimeProvider timeProvider)
{
    // Checks the answer to an Account input and keeps it for the run in the database's change tracker, replacing an
    // earlier answer to the input; the caller saves it with the rest of what it changes, so it is stored with the answer
    // or not at all. Input is the declaration from the run's own snapshot, never the sequence as it is now, since its
    // destination is what the password may reach. Null when kept; otherwise the problem, by the answer's field.
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

    // The account given for the input, with its password, or null when none was given. Password is null when it no
    // longer decrypts with this server's key ring.
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

    // The account inputs of the run that have an answer, ignoring case as input names do.
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

// Who gave an account for a run: the user signed in on the web or at the machine, and whether at the machine.
public sealed record RunCredentialGiver(Guid? UserId, string? Name, bool AtMachine);

// Field is the answer's field the problem is about: userName or password.
public sealed record RunCredentialProblem(string Field, ServerMessage Message);

// An account given for one run, as RunCredentials reads it back: the destination its input declared when it was given,
// who gave it and when.
public sealed record RunAccount(
    string InputName,
    string UserName,
    string? Password,
    string? Domain,
    IReadOnlyList<string> Hosts,
    bool RunAs,
    string? ProvidedByName,
    bool ProvidedAtMachine,
    DateTimeOffset GivenUtc)
{
    // A record prints every property by default, and the password must never reach a log.
    public override string ToString() =>
        $"RunAccount {{ InputName = {InputName}, UserName = {UserName}, Domain = {Domain}, RunAs = {RunAs}, ProvidedByName = {ProvidedByName} }}";
}
