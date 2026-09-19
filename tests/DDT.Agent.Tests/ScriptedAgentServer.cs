using DDT.Contracts.Agents;

namespace DDT.Agent.Tests;

// Answers from a script. When the register, next or sign-in script runs out it stops the loop, so every test
// ends deterministically without timing. Log requests succeed unless a scripted action throws.
internal sealed class ScriptedAgentServer : IAgentServer
{
    private readonly Queue<Func<AgentRegistration, AgentRegistrationResult>> _registrations = new();
    private readonly Queue<Func<string, AgentNextResult>> _nexts = new();
    private readonly Queue<Action<AgentLogBatch>> _logs = new();
    private readonly Queue<Func<AgentSignInRequest, AgentSignInResult>> _signIns = new();
    private readonly Queue<Func<AgentRelease?>> _releases = new();
    private readonly Queue<byte[]> _downloads = new();

    public CancellationTokenSource Stop { get; } = new();

    public List<string> Calls { get; } = [];

    public List<AgentRegistration> Registrations { get; } = [];

    public List<AgentLogLine> SentLines { get; } = [];

    public List<AgentSignInRequest> SignIns { get; } = [];

    public ScriptedAgentServer OnRegister(Func<AgentRegistration, AgentRegistrationResult> response)
    {
        _registrations.Enqueue(response);

        return this;
    }

    public ScriptedAgentServer OnNext(Func<string, AgentNextResult> response)
    {
        _nexts.Enqueue(response);

        return this;
    }

    public ScriptedAgentServer OnLog(Action<AgentLogBatch> action)
    {
        _logs.Enqueue(action);

        return this;
    }

    public ScriptedAgentServer OnSignIn(Func<AgentSignInRequest, AgentSignInResult> response)
    {
        _signIns.Enqueue(response);

        return this;
    }

    public ScriptedAgentServer OnRelease(Func<AgentRelease?> response)
    {
        _releases.Enqueue(response);

        return this;
    }

    public ScriptedAgentServer OnDownload(byte[] content)
    {
        _downloads.Enqueue(content);

        return this;
    }

    public Task<AgentRegistrationResult> RegisterAsync(AgentRegistration registration, CancellationToken cancellationToken)
    {
        Calls.Add("register");
        Registrations.Add(registration);

        return _registrations.TryDequeue(out Func<AgentRegistration, AgentRegistrationResult>? response)
            ? Task.FromResult(response(registration))
            : Stopped<AgentRegistrationResult>();
    }

    public Task<AgentNextResult> NextAsync(Guid machineId, string token, CancellationToken cancellationToken)
    {
        Calls.Add($"next {token}");

        return _nexts.TryDequeue(out Func<string, AgentNextResult>? response)
            ? Task.FromResult(response(token))
            : Stopped<AgentNextResult>();
    }

    public Task SendLogAsync(Guid machineId, string token, AgentLogBatch batch, CancellationToken cancellationToken)
    {
        Calls.Add($"log {token}");

        if (_logs.TryDequeue(out Action<AgentLogBatch>? action))
        {
            action(batch);
        }

        SentLines.AddRange(batch.Lines);

        return Task.CompletedTask;
    }

    public Task<AgentSignInResult> SignInAsync(Guid machineId, string token, AgentSignInRequest request, CancellationToken cancellationToken)
    {
        Calls.Add($"sign-in {token}");
        SignIns.Add(request);

        return _signIns.TryDequeue(out Func<AgentSignInRequest, AgentSignInResult>? response)
            ? Task.FromResult(response(request))
            : Stopped<AgentSignInResult>();
    }

    // Unlike the other calls, running out of answers means the server offers no agent, not the end of a test.
    public Task<AgentRelease?> GetReleaseAsync(CancellationToken cancellationToken)
    {
        Calls.Add("release");

        return Task.FromResult(_releases.TryDequeue(out Func<AgentRelease?>? response) ? response() : null);
    }

    public async Task DownloadReleaseAsync(Stream destination, CancellationToken cancellationToken)
    {
        Calls.Add("download");

        await destination.WriteAsync(_downloads.Dequeue(), cancellationToken);
    }

    private Task<T> Stopped<T>()
    {
        Stop.Cancel();

        return Task.FromCanceled<T>(Stop.Token);
    }
}
