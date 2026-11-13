// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Agent.Deployment;
using DDT.Agent.Sequences;
using DDT.Contracts.Agents;
using DDT.Contracts.Sequences;

namespace DDT.Agent.InstalledPhase;

// The service's calls to the server. Each retries, because the network may still be coming up.
internal sealed class InstalledPhaseCalls(
    IAgentServer server,
    IMachineIdentityReader identityReader,
    InstalledPhaseConsole console,
    InstalledPhaseOptions options,
    AgentLog log,
    TimeProvider timeProvider)
{
    private const int MaxLastBatches = 5;

    // Every attempt but the first waits a little first. Returns null when the server no longer has the run going on
    // this machine.
    public async Task<InstalledPhaseContact?> RegisterAsync(string? runToken, Guid runId, int attempt, CancellationToken cancellationToken)
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

        return new InstalledPhaseContact(registration.MachineId, new DeploymentTokens(token, resumeToken, registration.RunToken ?? runToken), identity);
    }

    // Returns null when the server couldn't be asked. The next registration sorts that out.
    public async Task<AgentNextResult?> AskAsync(InstalledPhaseContact contact, CancellationToken cancellationToken)
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
    public async Task<bool> ReportAsync(InstalledPhaseContact contact, Guid runId, AgentRunReport report, CancellationToken cancellationToken)
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

    // While the run still runs here, before its Done report. The settings hold a password, so they're kept in memory
    // only. A failure leaves them out, but the run is still done.
    public async Task FetchFirstLogonAsync(InstalledPhaseContact contact, AgentRun run, CancellationToken cancellationToken)
    {
        if (!run.SetsUpFirstLogon || console.KeepsFirstLogon)
        {
            return;
        }

        try
        {
            AgentFirstLogon read = await ServerCallRules.CallAsync(
                call => server.GetRunFirstLogonAsync(contact.MachineId, contact.Tokens.Token, run.Id, call),
                "the first-logon settings",
                log,
                timeProvider,
                cancellationToken).ConfigureAwait(false);

            if (read.IsEmpty)
            {
                log.Information("The answer file step that ran sets up nothing for the first sign-in.");

                return;
            }

            console.KeepFirstLogon(read);
            log.Information($"Once DDT's session is gone, the agent sets up {FirstLogonTexts.Summary(read)}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller ends the run as stopped
        }
        catch (Exception exception) when (exception is DeploymentStepException or AgentTokenRejectedException)
        {
            log.Warning($"The first-logon commands and the automatic logon could not be fetched ({LogText.OneLine(exception)}). Windows starts without them.");
        }
    }

    // For an agent that is being stopped, which gets no later moment. The system's own log has every line, so a line
    // that does not get through is not said again.
    public async Task SendLastLinesAsync(InstalledPhaseContact contact)
    {
        if (options.Profile.LastLines is not { } limit)
        {
            return;
        }

        using CancellationTokenSource deadline = new(limit, timeProvider);

        try
        {
            for (int batch = 0; batch < MaxLastBatches && log.QueuedLines > 0; batch++)
            {
                await log.FlushAsync(server, contact.MachineId, contact.Tokens.Token, deadline.Token).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or AgentTokenRejectedException || ServerCallRules.IsTransient(exception, deadline.Token))
        {
            // The agent ends now, and nothing could be done about it
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
        SequenceVersion: SupportedSequences.Version,
        Environment: options.Profile.Environment,
        SecureBootEnabled: identity.SecureBootEnabled,
        TrustedUefiCas: identity.TrustedUefiCas,
        ChassisType: identity.ChassisType,
        Facts: identity.Facts);
}
