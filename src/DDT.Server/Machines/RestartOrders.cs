// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Agents;
using DDT.Contracts.Deployments;
using DDT.Server.Data;
using DDT.Server.Deployments;

namespace DDT.Server.Machines;

// Orders the restarts for a run of the other boot environment, and stops after two that did not get the machine there.
internal sealed class RestartOrders(DdtDbContext database, RunQueries queries, BootEnvironments environments)
{
    // Nothing here saves. Netboot may never start the machine there: a BIOS, a DHCP server that names the boot file.
    public async Task<RestartOrder> ForAsync(Machine machine, Deployment run, string? address, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(machine);
        ArgumentNullException.ThrowIfNull(run);

        if (run.State != DeploymentState.Assigned)
        {
            return RestartOrder.None;
        }

        AgentEnvironment starts = await queries.StartsInAsync(run, cancellationToken).ConfigureAwait(false);

        if (starts == machine.AgentEnvironment)
        {
            return RestartOrder.None;
        }

        string where = $"{run.Title} runs in {PhaseRules.Name(starts)} and does not start in {PhaseRules.Name(machine.AgentEnvironment)}.";

        if (environments.Unreachable(machine, starts) is { } why)
        {
            return new RestartOrder(null, $"{where} {why}", false);
        }

        // The agent restarts as soon as it is told. One that asks again at once did not get the answer.
        if (PendingSwitch.IsOrdered(machine, starts, now))
        {
            return new RestartOrder(starts, null, false);
        }

        if (run.RestartsOrdered >= DeploymentLimits.RestartOrders)
        {
            bool open = machine.NextEnvironment is not null;
            PendingSwitch.End(machine);

            return new RestartOrder(null, $"{where} {GaveUp(starts)}", open);
        }

        Order(machine, run, starts, Actor.OfMachine(machine.Id, address), now);

        return new RestartOrder(starts, null, true);
    }

    // Recorded on the machine as a restart asked for at it is, so the web shows it and the resume token crosses over.
    private void Order(Machine machine, Deployment run, AgentEnvironment starts, Actor actor, DateTimeOffset now)
    {
        run.RestartsOrdered++;
        run.BootEnvironment ??= starts;
        machine.NextEnvironment = starts;
        machine.NextEnvironmentUtc = now;
        database.AuditEvents.Add(AuditEvents.Create(
            AuditActions.MachineEnvironmentRequested,
            machine.Id.ToString("D"),
            actor,
            now,
            $"Restarts from {PhaseRules.Name(machine.AgentEnvironment)} into {PhaseRules.Name(starts)} for {run.Title}, which is assigned to it. " +
            $"Restart {run.RestartsOrdered} of {DeploymentLimits.RestartOrders} the server orders for this run."));
    }

    private static string GaveUp(AgentEnvironment starts) =>
        $"The machine did not start {PhaseRules.Name(starts)} after restarting, so it is not restarted again. " +
        "Check that it starts from the network and that no DHCP server names another boot file. " +
        $"The run stays assigned and starts once the machine starts {PhaseRules.Name(starts)}.";
}
