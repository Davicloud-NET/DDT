// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Text.Json;

namespace DDT.Agent.Deployment;

// How a deployment treats its server calls. A 401 ends the run. Any other refusal fails the step with the server's
// reason. A failure that may pass on its own is tried again a few times, with a growing delay.
public static class ServerCallRules
{
    public const int MaxRetries = 5;

    public static async Task<T> CallAsync<T>(
        Func<CancellationToken, Task<T>> call,
        string what,
        AgentLog log,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(log);

        for (int failures = 0; ; failures++)
        {
            try
            {
                return await call(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsRefusal(exception))
            {
                throw new DeploymentStepException(Reason(exception, what), exception);
            }
            catch (Exception exception) when (IsTransient(exception, cancellationToken))
            {
                if (failures >= MaxRetries)
                {
                    throw new DeploymentStepException(
                        $"The server could not be reached for {what} after {MaxRetries + 1} attempts ({exception.Message}).",
                        exception);
                }

                TimeSpan delay = AgentLimits.RetryDelay(failures + 1);
                log.Warning($"Cannot reach the server for {what} ({exception.Message}). Trying again in {delay.TotalSeconds:0} s.");
                await Task.Delay(delay, timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    // A client error other than a timeout or a busy server. Asking again gets the same answer. A 401 never gets here,
    // because it arrives as AgentTokenRejectedException.
    public static bool IsRefusal(Exception exception) =>
        exception is HttpRequestException { StatusCode: { } status }
        && (int)status is >= 400 and < 500
        && status is not (HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests);

    public static bool IsTransient(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or IOException or TimeoutException or JsonException
        || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    // The server's problem title is a sentence for the operator. Without one, the status says what happened.
    public static string Reason(Exception exception, string what)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is AgentRequestException { ProblemTitle: { Length: > 0 } title })
        {
            return title;
        }

        return exception is HttpRequestException { StatusCode: { } status }
            ? $"The server refused {what} with {(int)status} {status}."
            : exception.Message;
    }
}
