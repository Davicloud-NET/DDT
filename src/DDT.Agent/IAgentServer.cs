using DDT.Contracts.Agents;

namespace DDT.Agent;

public interface IAgentServer
{
    Task<AgentRegistrationResult> RegisterAsync(AgentRegistration registration, CancellationToken cancellationToken);

    Task<AgentNextResult> NextAsync(Guid machineId, string token, CancellationToken cancellationToken);

    Task SendLogAsync(Guid machineId, string token, AgentLogBatch batch, CancellationToken cancellationToken);

    Task<AgentSignInResult> SignInAsync(Guid machineId, string token, AgentSignInRequest request, CancellationToken cancellationToken);
}
