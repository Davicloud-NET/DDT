using System.Text.Json;
using DDT.Contracts.Agents;
using DDT.Contracts.Machines;

namespace DDT.Agent;

public sealed class AgentLoop(
    IAgentServer server,
    IMachineIdentityReader identityReader,
    ISignInPrompt prompt,
    AgentLog log,
    TimeProvider timeProvider,
    string agentVersion)
{
    private MachineIdentity? _lastIdentity;
    private string? _resumeToken;

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        log.Information($"DDT agent {agentVersion}");

        if (!prompt.IsAvailable)
        {
            log.Information("Nobody can type at this console. Unless the server requires a sign in at the machine, approve it on the Machines page.");
        }

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

    // Returns null to register again, or an exit code. While the machine waits for someone to sign in at it,
    // only reading the keyboard runs alongside polling; every request stays on this loop, in order.
    private async Task<int?> PollAsync(AgentRegistrationResult registration, CancellationToken cancellationToken)
    {
        string token = registration.Token!;
        MachineState state = registration.State;
        string? signedInBy = registration.SignedInBy;
        TimeSpan interval = TimeSpan.FromSeconds(registration.PollAfterSeconds);
        int failures = 0;

        SignInConversation conversation = new(prompt, log);
        CancellationTokenSource? stopTyping = null;
        Task<string?>? typing = null;

        try
        {
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

                    if (next.SignedInBy != signedInBy)
                    {
                        signedInBy = next.SignedInBy;

                        if (state == MachineState.Pending && signedInBy is not null)
                        {
                            log.Information($"{signedInBy} signed in at this machine. An operator still has to approve it on the Machines page.");
                        }
                    }

                    if (state == MachineState.Pending && signedInBy is null && conversation.IsAvailable)
                    {
                        if (typing is null)
                        {
                            stopTyping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                            typing = conversation.ReadAsync(stopTyping.Token);
                        }
                    }
                    else if (typing is not null)
                    {
                        await StopTypingAsync(stopTyping!, typing).ConfigureAwait(false);
                        typing = null;
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
                    interval = AgentLimits.RetryDelay(failures);
                    log.Warning($"Cannot reach the server ({exception.Message}). Retrying in {interval.TotalSeconds:0} s.");
                }

                if (typing is null)
                {
                    if (!await DelayAsync(interval, cancellationToken).ConfigureAwait(false))
                    {
                        return AgentExitCodes.Stopped;
                    }

                    continue;
                }

                using (CancellationTokenSource waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    Task elapsed = Task.Delay(interval, timeProvider, waiting.Token);

                    if (await Task.WhenAny(typing, elapsed).ConfigureAwait(false) != typing)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            return AgentExitCodes.Stopped;
                        }

                        continue;
                    }

                    await waiting.CancelAsync().ConfigureAwait(false);
                }

                string? typed = await typing.ConfigureAwait(false);
                stopTyping!.Dispose();
                stopTyping = null;
                typing = null;

                if (typed is null || conversation.Accept(typed) is not { } request)
                {
                    // Poll before asking for the next field, so an approval on the web is noticed right away.
                    continue;
                }

                try
                {
                    AgentSignInResult result = await server.SignInAsync(registration.MachineId, token, request, cancellationToken).ConfigureAwait(false);
                    conversation.Handle(result.Status);
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
                    conversation.NotSent(exception);
                }
            }

            return AgentExitCodes.Stopped;
        }
        finally
        {
            if (typing is not null)
            {
                await StopTypingAsync(stopTyping!, typing).ConfigureAwait(false);
            }
        }
    }

    // Waits for the prompt to let go of the console before anything else can ask for input.
    private static async Task StopTypingAsync(CancellationTokenSource stopTyping, Task<string?> typing)
    {
        await stopTyping.CancelAsync().ConfigureAwait(false);
        await typing.ConfigureAwait(false);
        stopTyping.Dispose();
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
                log.Warning("The server refused the registration. Trying again.");
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

            if (!await DelayAsync(AgentLimits.RetryDelay(failures), cancellationToken).ConfigureAwait(false))
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

    private static string Describe(MachineState state) => state switch
    {
        MachineState.Pending => "waiting to be authorized",
        MachineState.Approved => "approved, with nothing assigned yet",
        _ => state.ToString(),
    };
}
