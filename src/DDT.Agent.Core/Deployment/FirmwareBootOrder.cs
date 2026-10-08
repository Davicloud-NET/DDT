// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;

namespace DDT.Agent.Deployment;

// Changes the firmware's boot order through its variables, and remembers what a run changed to put it back.
public sealed class FirmwareBootOrder(IUefiVariables variables, AgentLog log) : IBootOrder
{
    // What the current run changed.
    private UndoableUefiVariables? _changes;

    // The boot environment that changes the order, for what putting it back keeps the machine from starting.
    public AgentEnvironment Environment { get; init; } = AgentEnvironment.WindowsPE;

    // A new run starts, so nothing an earlier run changed is ever put back.
    public void Forget() => _changes = null;

    public Task PutFirstAsync(
        EspPartition esp,
        string loaderPath,
        string description,
        IReadOnlyCollection<Guid> erasedSystemPartitionIds,
        CancellationToken cancellationToken)
    {
        try
        {
            MakeFirst(() => esp, loaderPath, description, erasedSystemPartitionIds);
        }
        catch (Exception exception)
        {
            log.Warning(
                $"{description} could not be put first in the firmware boot order ({exception.Message}), so this machine may start " +
                "from the network again. Set its boot order to start the disk first.");
        }

        return Task.CompletedTask;
    }

    // Throws what finding the partition or the firmware throws. RestoreAsync still puts back what was changed by then.
    public void MakeFirst(Func<EspPartition> esp, string loaderPath, string description, IReadOnlyCollection<Guid> erasedSystemPartitionIds)
    {
        ArgumentNullException.ThrowIfNull(esp);

        UndoableUefiVariables changes = new(variables);
        _changes = changes;
        new FirmwareBootEntry(changes, log).MakeFirst(esp(), loaderPath, description, erasedSystemPartitionIds);
    }

    public Task RestoreAsync(CancellationToken cancellationToken)
    {
        UndoableUefiVariables? changes = _changes;
        _changes = null;

        try
        {
            if (changes is not null && changes.Undo())
            {
                log.Information(EnvironmentTexts.BootOrderIsBack(Environment));
            }
        }
        catch (Exception exception)
        {
            log.Warning(EnvironmentTexts.BootOrderStays(Environment, exception.Message));
        }

        return Task.CompletedTask;
    }
}
