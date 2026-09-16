using System.Net.Http.Headers;
using System.Net.Http.Json;
using DDT.Contracts.Agents;

namespace DDT.Server.Tests;

// Speaks to the agent endpoints the way DDT.Agent does: bearer tokens, no cookies.
public sealed class AgentClient(HttpClient client) : IDisposable
{
    public static AgentRegistration Registration(string uuid, string mac, params string[] otherMacs) =>
        new(uuid, mac, [mac, .. otherMacs], "Microsoft Corporation", "Virtual Machine", "0000-0000", "1.0.0");

    public Task<HttpResponseMessage> RegisterAsync(string? enrollmentToken, AgentRegistration registration) =>
        SendAsync(HttpMethod.Post, AgentRoutes.Register, enrollmentToken, JsonContent.Create(registration, options: TestJson.Options));

    public Task<HttpResponseMessage> NextAsync(Guid machineId, string token) =>
        SendAsync(HttpMethod.Get, AgentRoutes.Next(machineId), token, null);

    public Task<HttpResponseMessage> LogAsync(Guid machineId, string token, AgentLogBatch batch) =>
        SendAsync(HttpMethod.Post, AgentRoutes.Log(machineId), token, JsonContent.Create(batch, options: TestJson.Options));

    public void Dispose() => client.Dispose();

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? token, HttpContent? content)
    {
        using HttpRequestMessage request = new(method, new Uri(path, UriKind.Relative)) { Content = content };

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await client.SendAsync(request);
    }
}
