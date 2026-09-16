using System.Text.Json;
using DDT.Contracts.Agents;
using DDT.Contracts.Machines;

namespace DDT.Agent;

public sealed class AgentLoop(
    IAgentServer server,
    IMachineIdentityReader identityReader,
    AgentLog log,
    TimeProvider timeProvider,
    string agentVersion)
{
    private MachineIdentity? _lastIdentity;
    private string? _resumeToken;

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        log.Information($"DDT agent {agentVersion}");

        bool first = true;

        while (!cancellationToken.IsCancellationRequested)
        {
            // After a refused token, never register again in a tight loop: two agents fighting over one
            // machine would otherwise hammer the server.
            if (!first && !await DelayAsync(AgentLimits.MinRetryDelay, cancellationToken).ConfigureAwait(false))
            {
                return AgentExitCodes.Stopped;
            }

            first = false;

            AgentRegistrationResult? registration = await RegisterAsync(cancellationToken).ConfigureAwait(false);

            if (registration is null)
            {
                return AgentExitCodes.Stopped;
            }

            if (registration.Token is null)
            {
                log.Error($"An administrator rejected machine {registration.MachineId}. The agent stops here.");

                return AgentExitCodes.Rejected;
            }

            _resumeToken = registration.ResumeToken;
            log.Information($"Registered as machine {registration.MachineId}, {Describe(registration.State)}");

            int? exitCode = await PollAsync(registration, cancellationToken).ConfigureAwait(false);

            if (exitCode is { } code)
            {
                return code;
            }
        }

        return AgentExitCodes.Stopped;
    }

    // Returns null to register again, or an exit code.
    private async Task<int?> PollAsync(AgentRegistrationResult registration, CancellationToken cancellationToken)
    {
        string token = registration.Token!;
        MachineState state = registration.State;
        TimeSpan interval = TimeSpan.FromSeconds(registration.PollAfterSeconds);
        int failures = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                AgentNextResult next = await server.NextAsync(registration.MachineId, token, cancellationToken).ConfigureAwait(false);

                failures = 0;
                interval = TimeSpan.FromSeconds(next.PollAfterSeconds);
                token = next.Token;
                _resumeToken = next.ResumeToken;

                if (next.State != state)
                {
                    state = next.State;
                    log.Information($"Machine is now {Describe(state)}");
                }

                // Only an approved machine may write to the server's log. Until then lines wait here.
                if (state is MachineState.Approved or MachineState.Deploying)
                {
                    await FlushAsync(registration.MachineId, token, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (AgentTokenRejectedException)
            {
                log.Warning("The server no longer accepts this machine's token. Registering again.");

                return null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return AgentExitCodes.Stopped;
            }
            catch (Exception exception) when (IsTransient(exception))
            {
                failures++;
                interval = RetryDelay(failures);
                log.Warning($"Cannot reach the server ({exception.Message}). Retrying in {interval.TotalSeconds:0} s.");
            }

            if (!await DelayAsync(interval, cancellationToken).ConfigureAwait(false))
            {
                return AgentExitCodes.Stopped;
            }
        }

        return AgentExitCodes.Stopped;
    }

    // A log that cannot be delivered must not stop the machine from polling, so a failure other than a
    // refused token leaves the lines queued for the next attempt and says nothing: a warning per failed
    // flush would itself fill the queue.
    private async Task FlushAsync(Guid machineId, string token, CancellationToken cancellationToken)
    {
        try
        {
            await log.FlushAsync(server, machineId, token, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (IsTransient(exception) && !cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task<AgentRegistrationResult?> RegisterAsync(CancellationToken cancellationToken)
    {
        int failures = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Read again on every attempt: the adapter with the default route, which is the primary MAC,
                // may only be known once DHCP has finished.
                MachineIdentity identity = identityReader.Read();
                ReportIdentity(identity);

                return await server.RegisterAsync(
                    new AgentRegistration(
                        identity.SmbiosUuid,
                        identity.PrimaryMac,
                        identity.MacAddresses,
                        identity.Manufacturer,
                        identity.Model,
                        identity.SerialNumber,
                        agentVersion,
                        _resumeToken),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (AgentTokenRejectedException)
            {
                failures++;
                log.Error("The server refused the enrollment token. It may have expired or been revoked; rebuild the boot image with a current one.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            catch (Exception exception) when (IsTransient(exception))
            {
                failures++;
                log.Warning($"Cannot register with the server ({exception.Message}).");
            }

            if (!await DelayAsync(RetryDelay(failures), cancellationToken).ConfigureAwait(false))
            {
                return null;
            }
        }

        return null;
    }

    private void ReportIdentity(MachineIdentity identity)
    {
        if (_lastIdentity is not null
            && _lastIdentity.SmbiosUuid == identity.SmbiosUuid
            && _lastIdentity.PrimaryMac == identity.PrimaryMac)
        {
            return;
        }

        _lastIdentity = identity;
        log.Information($"SMBIOS UUID {identity.SmbiosUuid}, primary MAC {identity.PrimaryMac}");
        log.Information($"{identity.Manufacturer} {identity.Model}, serial {identity.SerialNumber}");
    }

    // JsonException covers an HTML page from a wrong URL and a newer server reporting a state this agent
    // does not know; neither may end the agent.
    private static bool IsTransient(Exception exception) =>
        exception is HttpRequestException or TimeoutException or TaskCanceledException or JsonException;

    private async Task<bool> DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, timeProvider, cancellationToken).ConfigureAwait(false);

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static TimeSpan RetryDelay(int failures)
    {
        TimeSpan delay = AgentLimits.MinRetryDelay * (1 << Math.Min(failures - 1, 4));

        return delay < AgentLimits.MaxRetryDelay ? delay : AgentLimits.MaxRetryDelay;
    }

    private static string Describe(MachineState state) => state switch
    {
        MachineState.Pending => "waiting for an administrator to approve it",
        MachineState.Approved => "approved, with nothing assigned yet",
        _ => state.ToString(),
    };
}
