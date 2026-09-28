// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;

namespace DDT.Agent;

// Registers the machine with what it reads of it, and tries again until the server answers.
internal sealed class AgentRegistrar(IAgentServer server, AgentMachine machine, ConsoleStatus status, AgentLog log, TimeProvider timeProvider)
{
    // Kept, as the run's conditions test it.
    public MachineIdentity? LastIdentity { get; private set; }

    // Null once stopped.
    public async Task<AgentRegistrationResult?> RegisterAsync(string? resumeToken, string? runToken, CancellationToken cancellationToken)
    {
        int failures = 0;
        status.Registering();
        IReadOnlyList<AgentDisk>? eligibleDisks = await ReadDisksAsync(cancellationToken).ConfigureAwait(false);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Read again on every attempt: the adapter with the default route, which is the primary MAC,
                // may only be known once DHCP has finished.
                MachineIdentity identity = machine.Identity.Read();
                ReportIdentity(identity);

                return await server.RegisterAsync(Registration(identity, eligibleDisks, resumeToken, runToken), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (AgentTokenRejectedException exception)
            {
                failures++;
                log.Warning("The server refused the registration. Trying again.");
                status.Unreachable(exception);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            catch (Exception exception) when (LoopCallRules.IsTransient(exception))
            {
                failures++;
                log.Warning($"Cannot register with the server ({exception.Message}).");
                status.Unreachable(exception);
            }

            if (!await CancellableDelay.WaitAsync(AgentLimits.RetryDelay(failures), timeProvider, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }
        }

        return null;
    }

    private AgentRegistration Registration(
        MachineIdentity identity,
        IReadOnlyList<AgentDisk>? eligibleDisks,
        string? resumeToken,
        string? runToken) =>
        new(
            identity.SmbiosUuid,
            identity.PrimaryMac,
            identity.MacAddresses,
            identity.Manufacturer,
            identity.Model,
            identity.SerialNumber,
            machine.AgentVersion,
            resumeToken,
            eligibleDisks,
            runToken,
            SequenceDefinition.CurrentVersion,
            SecureBootEnabled: identity.SecureBootEnabled,
            TrustedUefiCas: identity.TrustedUefiCas,
            ChassisType: identity.ChassisType,
            Facts: identity.Facts);

    // The server refuses a web assignment to a machine with several disks, so it has to know them. A machine whose
    // disks cannot be read registers without them rather than not at all.
    private async Task<IReadOnlyList<AgentDisk>?> ReadDisksAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<LocalDisk> eligible = await machine.Disks.ListDisksAsync(cancellationToken).ConfigureAwait(false);
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
