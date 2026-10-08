// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Consoles;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;

namespace DDT.Agent;

// Restarts the machine into the other boot environment with its resume token in the firmware, once per start.
internal sealed class BootSwitch(IAgentServer server, AgentMachine machine, ConsoleStatus status, AgentLog log)
{
    private bool _taken;
    private AgentEnvironment? _refused;

    // The resume token the agent before the restart left in the firmware. It is there once, before the first
    // registration, and null on every later call.
    public string? TakeCarried()
    {
        if (_taken)
        {
            return null;
        }

        _taken = true;

        return machine.Switch?.Carrier.Take();
    }

    // For a sequence picked at the machine. The server keeps the pick, so the agent there offers the sequence first.
    // No disk, name or answer was asked yet. True if the machine restarts.
    public async Task<bool> RestartForPickAsync(Guid machineId, DeploymentTokens tokens, AgentEnvironmentRequest pick, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(pick);

        if (!CanRestartInto(pick.Environment, again: true))
        {
            return false;
        }

        try
        {
            await server.RequestEnvironmentAsync(machineId, tokens.Token, pick, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (ServerCallRules.IsRefusal(exception))
        {
            log.Warning($"The server did not accept the restart: {ServerCallRules.Reason(exception, "the restart")}");

            return false;
        }
        catch (Exception exception) when (LoopCallRules.IsTransient(exception) && !cancellationToken.IsCancellationRequested)
        {
            log.Warning($"Cannot tell the server of the restart ({exception.Message}). Choose the sequence again.");

            return false;
        }

        return await RestartAsync(pick.Environment, tokens, cancellationToken).ConfigureAwait(false);
    }

    // For a run assigned on the web or by a rule, which every poll answer names until the machine is where the run
    // starts. letGo runs first, only when the machine does restart. True if it restarts.
    public async Task<bool> RestartForRunAsync(AgentEnvironment target, DeploymentTokens tokens, Func<Task> letGo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(letGo);

        if (!CanRestartInto(target, again: false))
        {
            return false;
        }

        await letGo().ConfigureAwait(false);

        return await RestartAsync(target, tokens, cancellationToken).ConfigureAwait(false);
    }

    // Says why not. A poll asks every few seconds, so it is told once per environment unless again is set.
    private bool CanRestartInto(AgentEnvironment target, bool again)
    {
        string? why = machine.Switch switch
        {
            null => $"This agent cannot restart the machine into {AgentEnvironments.Name(target)}.",
            { Carrier.SwitchFailed: true } =>
                $"The machine is not restarted into {AgentEnvironments.Name(target)} again: the server could not start it when the machine last " +
                "restarted for it. Start the machine from the network anew to try again.",
            _ => null,
        };

        if (why is not null && (again || _refused != target))
        {
            _refused = target;
            log.Warning(why);
        }

        return why is null;
    }

    private async Task<bool> RestartAsync(AgentEnvironment target, DeploymentTokens tokens, CancellationToken cancellationToken)
    {
        if (machine.Switch is not { } change)
        {
            return false;
        }

        // A token the firmware did not keep costs a second sign-in, not the restart.
        bool carried = change.Carrier.Put(target, tokens.ResumeToken);
        string name = AgentEnvironments.Name(target);
        log.Information(carried
            ? $"The machine restarts into {name}, where it stays signed in and goes on."
            : $"The machine restarts into {name}.");

        try
        {
            await change.Rebooter.RebootAsync(RestartInto.BootEnvironment, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The agent ends here either way, so the failure is said and not thrown on
            string message = $"The machine could not restart itself ({LogText.OneLine(exception)}). Restart it by hand; it then starts {name}.";
            log.Error(message);
            status.RestartFailed(message);
        }

        return true;
    }
}
