using System.Net.Http.Json;
using DDT.Contracts.Agents;
using Xunit;

namespace DDT.Server.Tests;

// A machine that has registered and holds its first poll token, as the agent does right before it asks the
// technician to sign in.
public sealed class RegisteredMachine(
    AgentClient agent,
    string enrollmentToken,
    AgentRegistration registration,
    AgentRegistrationResult registered) : IDisposable
{
    public AgentClient Agent => agent;

    public string EnrollmentToken => enrollmentToken;

    public AgentRegistration Registration => registration;

    public Guid Id => registered.MachineId;

    public string PollToken => registered.Token!;

    public async Task<AgentSignInResult> SignInAsync(string userName, string password = DdtApplication.Password, string? code = null) =>
        await ReadAsync<AgentSignInResult>(await agent.SignInAsync(Id, PollToken, new AgentSignInRequest(userName, password, code)));

    public async Task<AgentNextResult> NextAsync() =>
        await ReadAsync<AgentNextResult>(await agent.NextAsync(Id, PollToken));

    public static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");

        return (await response.Content.ReadFromJsonAsync<T>(TestJson.Options))!;
    }

    public void Dispose() => agent.Dispose();
}
