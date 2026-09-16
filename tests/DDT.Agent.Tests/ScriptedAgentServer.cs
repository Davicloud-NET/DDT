using DDT.Contracts.Agents;

namespace DDT.Agent.Tests;

// Answers from a script. When the register or next script runs out it stops the loop, so every test ends
// deterministically without timing. Log requests succeed unless a scripted action throws.
internal sealed class ScriptedAgentServer : IAgentServer
{
    private readonly Queue<Func<AgentRegistration, AgentRegistrationResult>> _registrations = new();
    private readonly Queue<Func<string, AgentNextResult>> _nexts = new();
    private readonly Queue<Action<AgentLogBatch>> _logs = new();

    public CancellationTokenSource Stop { get; } = new();

    public List<string> Calls { get; } = [];

    public List<AgentRegistration> Registrations { get; } = [];

    public List<AgentLogLine> SentLines { get; } = [];

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

    private Task<T> Stopped<T>()
    {
        Stop.Cancel();

        return Task.FromCanceled<T>(Stop.Token);
    }
}
