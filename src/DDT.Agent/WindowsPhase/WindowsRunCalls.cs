// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Agent.Deployment;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;

namespace DDT.Agent.WindowsPhase;

// The service's calls to the server: the registration with the run token, the question whether the run continues, and
// the run's last report. The network may still be coming up while Windows starts, so each call retries.
internal sealed class WindowsRunCalls(
    IAgentServer server,
    IMachineIdentityReader identityReader,
    WindowsPhaseConsole console,
    WindowsPhaseOptions options,
    AgentLog log,
    TimeProvider timeProvider)
{
    // Every attempt but the first waits a little first. Returns null when the server no longer has the run going on
    // this machine.
    public async Task<WindowsRunContact?> RegisterAsync(string? runToken, Guid runId, int attempt, CancellationToken cancellationToken)
    {
        if (attempt > 0)
        {
            await Task.Delay(AgentLimits.RetryDelay(attempt), timeProvider, cancellationToken).ConfigureAwait(false);
        }

        if (await RegisterUntilAnsweredAsync(runToken, cancellationToken).ConfigureAwait(false)
                is not ({ Token: { } token, ResumeToken: { } resumeToken } registration, var identity)
            || registration.RunId != runId)
        {
            return null;
        }

        await console.RegisteredAsync(registration, cancellationToken).ConfigureAwait(false);

        return new WindowsRunContact(registration.MachineId, new DeploymentTokens(token, resumeToken, registration.RunToken ?? runToken), identity);
    }

    // Returns null when the server couldn't be asked. The next registration sorts that out.
    public async Task<AgentNextResult?> AskAsync(WindowsRunContact contact, CancellationToken cancellationToken)
    {
        try
        {
            AgentNextResult next = await server.NextAsync(contact.MachineId, contact.Tokens.Token, cancellationToken).ConfigureAwait(false);
            contact.Tokens.Update(next.Token, next.ResumeToken);

            return next;
        }
        catch (Exception exception) when (exception is AgentTokenRejectedException || ServerCallRules.IsTransient(exception, cancellationToken))
        {
            log.Warning($"Cannot ask the server about the run ({exception.Message}).");

            return null;
        }
    }

    // True once the server has the report, or refused it.
    public async Task<bool> ReportAsync(WindowsRunContact contact, Guid runId, AgentRunReport report, CancellationToken cancellationToken)
    {
        try
        {
            await server.ReportRunAsync(contact.MachineId, contact.Tokens.Token, runId, report, cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (Exception exception) when (ServerCallRules.IsRefusal(exception))
        {
            log.Warning(ServerCallRules.Reason(exception, "the run's last report"));

            return true;
        }
        catch (Exception exception) when (exception is AgentTokenRejectedException || ServerCallRules.IsTransient(exception, cancellationToken))
        {
            log.Warning($"The run's last report could not be sent ({exception.Message}). Trying again.");

            return false;
        }
    }

    // Returns the registration with the identity it used. Null when the server has nothing for this service.
    private async Task<(AgentRegistrationResult Registration, MachineIdentity Identity)?> RegisterUntilAnsweredAsync(
        string? runToken,
        CancellationToken cancellationToken)
    {
        console.Status?.Registering();

        for (int failures = 0; ; failures++)
        {
            if (failures > 0)
            {
                await Task.Delay(AgentLimits.RetryDelay(failures), timeProvider, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                MachineIdentity identity = identityReader.Read();
                console.Status?.Identified(identity);

                return (await server.RegisterAsync(Registration(identity, runToken), cancellationToken).ConfigureAwait(false), identity);
            }
            catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
            {
                log.Information(ServerCallRules.Reason(exception, "the registration"));

                return null;
            }
            catch (Exception exception) when (exception is AgentTokenRejectedException || ServerCallRules.IsTransient(exception, cancellationToken))
            {
                log.Warning($"Cannot register with the server ({exception.Message}).");
                console.Status?.Unreachable(exception);
            }
        }
    }

    private AgentRegistration Registration(MachineIdentity identity, string? runToken) => new(
        identity.SmbiosUuid,
        identity.PrimaryMac,
        identity.MacAddresses,
        identity.Manufacturer,
        identity.Model,
        identity.SerialNumber,
        options.AgentVersion,
        RunToken: runToken,
        SequenceVersion: SequenceDefinition.CurrentVersion,
        Environment: AgentEnvironment.Windows,
        SecureBootEnabled: identity.SecureBootEnabled,
        TrustedUefiCas: identity.TrustedUefiCas,
        ChassisType: identity.ChassisType,
        Facts: identity.Facts);
}
