// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;

namespace DDT.Agent;

// Registers the machine with the facts it reads, and tries again until the server answers.
internal sealed class AgentRegistrar(IAgentServer server, AgentMachine machine, ConsoleStatus status, AgentLog log, TimeProvider timeProvider)
{
    private bool _disksSaid;

    // Kept because the run's conditions test it.
    public MachineIdentity? LastIdentity { get; private set; }

    // Lists the disks without a line each once a registration of this registrar has listed them on the screen. Null
    // to list them with the machine's own each time.
    public IDisks? QuietDisks { get; init; }

    // Null once stopped.
    public Task<AgentRegistrationResult?> RegisterAsync(string? resumeToken, string? runToken, CancellationToken cancellationToken) =>
        RegisterAsync(new RegistrationClaims { ResumeToken = resumeToken, RunToken = runToken }, cancellationToken);

    // Null once stopped, and once the time the claims give the server is over without an answer.
    public async Task<AgentRegistrationResult?> RegisterAsync(RegistrationClaims claims, CancellationToken cancellationToken)
    {
        long began = timeProvider.GetTimestamp();
        int failures = 0;
        bool toldNoAdapter = false;
        status.Registering();
        IReadOnlyList<AgentDisk>? eligibleDisks = await ReadDisksAsync(cancellationToken).ConfigureAwait(false);

        while (!cancellationToken.IsCancellationRequested)
        {
            TimeSpan? left = Left(claims, began);

            if (left <= TimeSpan.Zero)
            {
                break;
            }

            // The time that is left ends a try too. A server that takes the call and says nothing would double it.
            using (CancellationTokenSource timeUp = new(Timeout.InfiniteTimeSpan, timeProvider))
            using (CancellationTokenSource trying = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeUp.Token))
            {
                timeUp.CancelAfter(left ?? Timeout.InfiniteTimeSpan);

                try
                {
                    // Read it again on every attempt. The primary MAC is the adapter with the default route, and that
                    // may only be known once DHCP has finished.
                    MachineIdentity identity = machine.Identity.Read();
                    ReportIdentity(identity);

                    return await server.RegisterAsync(Registration(identity, eligibleDisks, claims), trying.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || timeUp.IsCancellationRequested)
                {
                    return null;
                }
                catch (Exception exception) when (exception is AgentTokenRejectedException or NoNetworkAdapterException || LoopCallRules.IsTransient(exception))
                {
                    failures++;
                    Say(exception, ref toldNoAdapter);
                }
            }

            if (!await CancellableDelay.WaitAsync(Wait(failures, Left(claims, began)), timeProvider, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }
        }

        return null;
    }

    // The time the claims still give the server. Null when they give it all the time it takes.
    private TimeSpan? Left(RegistrationClaims claims, long began) => claims.GiveUpAfter - timeProvider.GetElapsedTime(began);

    // No wait goes past the time that is left.
    private static TimeSpan Wait(int failures, TimeSpan? left)
    {
        TimeSpan delay = AgentLimits.RetryDelay(failures);

        return left is not { } rest || rest >= delay ? delay : rest > TimeSpan.Zero ? rest : TimeSpan.Zero;
    }

    // An adapter on USB or in a dock may come later, so a machine without one says so once and asks again.
    private void Say(Exception exception, ref bool toldNoAdapter)
    {
        switch (exception)
        {
            case AgentTokenRejectedException:
                log.Warning("The server refused the registration. Trying again.");
                break;
            case NoNetworkAdapterException when toldNoAdapter:
                break;
            case NoNetworkAdapterException:
                toldNoAdapter = true;
                log.Warning(exception.Message);
                break;
            default:
                log.Warning($"Cannot register with the server ({exception.Message}).");
                break;
        }

        status.Unreachable(exception);
    }

    private AgentRegistration Registration(MachineIdentity identity, IReadOnlyList<AgentDisk>? eligibleDisks, RegistrationClaims claims) =>
        new(
            identity.SmbiosUuid,
            identity.PrimaryMac,
            identity.MacAddresses,
            identity.Manufacturer,
            identity.Model,
            identity.SerialNumber,
            machine.AgentVersion,
            claims.ResumeToken,
            eligibleDisks,
            claims.RunToken,
            SupportedSequences.Version,
            machine.Environment,
            SecureBootEnabled: identity.SecureBootEnabled,
            TrustedUefiCas: identity.TrustedUefiCas,
            ChassisType: identity.ChassisType,
            Facts: identity.Facts,
            StartsOver: claims.StartsOver ? true : null,
            HandedOver: claims.HandedOver);

    // The server refuses a web assignment to a machine with several disks, so it has to know the disks. If they can't
    // be read, the machine registers without them rather than not at all.
    private async Task<IReadOnlyList<AgentDisk>?> ReadDisksAsync(CancellationToken cancellationToken)
    {
        try
        {
            IDisks disks = _disksSaid && QuietDisks is { } quiet ? quiet : machine.Disks;
            IReadOnlyList<LocalDisk> eligible = await disks.ListDisksAsync(cancellationToken).ConfigureAwait(false);
            _disksSaid = true;
            status.DisksRead(eligible);

            return [.. eligible.Select(disk => disk.ToAgentDisk())];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            log.Warning($"The disks cannot be read ({exception.Message}). Registering without them.");

            return null;
        }
    }

    private void ReportIdentity(MachineIdentity identity)
    {
        bool known = LastIdentity is not null
            && LastIdentity.SmbiosUuid == identity.SmbiosUuid
            && LastIdentity.PrimaryMac == identity.PrimaryMac;

        LastIdentity = identity;
        status.Identified(identity);

        if (known)
        {
            return;
        }

        log.Information($"SMBIOS UUID {identity.SmbiosUuid}, primary MAC {identity.PrimaryMac}");
        log.Information($"{identity.Manufacturer} {identity.Model}, serial {identity.SerialNumber}");
    }
}
