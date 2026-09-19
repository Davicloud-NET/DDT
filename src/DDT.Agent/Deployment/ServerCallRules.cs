using System.Net;
using System.Text.Json;

namespace DDT.Agent.Deployment;

// How a deployment treats its server calls: a 401 ends the run, any other refusal fails the step with the server's
// reason, and whatever may pass on its own is tried again with a growing delay, a few times at most.
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

    // A client error other than a timeout or a busy server: asking again gets the same answer. A 401 never gets
    // here, it arrives as AgentTokenRejectedException.
    public static bool IsRefusal(Exception exception) =>
        exception is HttpRequestException { StatusCode: { } status }
        && (int)status is >= 400 and < 500
        && status is not (HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests);

    public static bool IsTransient(Exception exception, CancellationToken cancellationToken) =>
        exception is HttpRequestException or IOException or TimeoutException or JsonException
        || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    // The server's problem title is a sentence for the operator; without one, the status says what happened.
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
