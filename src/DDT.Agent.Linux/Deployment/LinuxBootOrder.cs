// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Agent.Deployment;

// The firmware's boot order in the Linux boot environment. After a change it lists the firmware's entries for the
// machine log, where Windows PE lists them with bcdedit.
public sealed class LinuxBootOrder(IUefiVariables variables, AgentLog log) : IBootOrder
{
    private readonly FirmwareBootOrder _bootOrder = new(variables, log) { Environment = AgentEnvironment.LinuxBoot };

    public async Task PutFirstAsync(
        EspPartition esp,
        string loaderPath,
        string description,
        IReadOnlyCollection<Guid> erasedSystemPartitionIds,
        CancellationToken cancellationToken)
    {
        await _bootOrder.PutFirstAsync(esp, loaderPath, description, erasedSystemPartitionIds, cancellationToken).ConfigureAwait(false);
        ListEntries();
    }

    public async Task RestoreAsync(CancellationToken cancellationToken)
    {
        await _bootOrder.RestoreAsync(cancellationToken).ConfigureAwait(false);
        ListEntries();
    }

    // Only for the machine log.
    private void ListEntries()
    {
        try
        {
            foreach (string line in FirmwareBootEntries.Describe(variables))
            {
                log.Information(line);
            }
        }
        catch (DeploymentStepException exception)
        {
            log.Warning($"The firmware boot entries cannot be listed ({exception.Message}).");
        }
    }
}
