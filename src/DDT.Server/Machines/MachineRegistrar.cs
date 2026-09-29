// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Contracts.Agents;
using DDT.Contracts.Machines;
using DDT.Server.Data;
using DDT.Server.Deployments;
using DDT.Server.Settings;
using Microsoft.EntityFrameworkCore;

namespace DDT.Server.Machines;

// Registers the agent of a netbooting machine, or of the service in its installed Windows, and hands out its tokens.
public sealed class MachineRegistrar(
    DdtDbContext database,
    MachineTokenService tokens,
    MachineArrivals arrivals,
    RegistrationPublisher publisher,
    DdtSettings settings,
    TimeProvider timeProvider)
{
    public const int PollAfterSeconds = 10;

    private const int MaxAttempts = 3;

    public async Task<MachineRegistration> RegisterAsync(
        NormalisedRegistration registration,
        IPAddress? remoteAddress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(registration);

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await TryRegisterAsync(registration, remoteAddress, cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
            {
                // An approval, rejection, assignment, report or other registration changed the machine in between.
                // Decide again from what is stored now rather than overwrite it.
                database.ChangeTracker.Clear();
            }
        }
    }

    private async Task<MachineRegistration> TryRegisterAsync(
        NormalisedRegistration registration,
        IPAddress? remoteAddress,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        Arrival arrival = await arrivals.RecordAsync(registration, remoteAddress, now, cancellationToken).ConfigureAwait(false);

        if (arrival.Machine is not { } machine)
        {
            return MachineRegistration.Refused(arrival.Refusal);
        }

        registration.ApplyTo(machine, remoteAddress?.ToString(), now);
        await publisher.PublishAsync(machine, arrival, cancellationToken).ConfigureAwait(false);

        return new MachineRegistration(Result(machine, arrival.Continued), RegistrationRefusal.None);
    }

    // A rejected machine gets no token at all.
    private AgentRegistrationResult Result(Machine machine, Deployment? continued)
    {
        string? consoleLanguage = settings.Current.Deployment.ConsoleLanguage;

        return machine.State == MachineState.Rejected
            ? new AgentRegistrationResult(machine.Id, machine.State, null, null, PollAfterSeconds, null, ConsoleLanguage: consoleLanguage)
            : new AgentRegistrationResult(
                machine.Id,
                machine.State,
                tokens.IssueCurrent(machine),
                tokens.Issue(machine, MachineTokenPurpose.Resume),
                PollAfterSeconds,
                machine.SignedInUserName,
                continued?.Id,
                continued is null ? null : tokens.IssueRunToken(machine, continued.Id),
                consoleLanguage);
    }
}
