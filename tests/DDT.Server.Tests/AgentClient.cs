using System.Net.Http.Headers;
using System.Net.Http.Json;
using DDT.Contracts.Agents;

namespace DDT.Server.Tests;

// Speaks to the agent endpoints the way DDT.Agent does: bearer tokens, no cookies. A remote address gives the
// client its own rate limit partitions.
public sealed class AgentClient(HttpClient client, string? remoteAddress = null) : IDisposable
{
    public static AgentRegistration Registration(string uuid, string mac, params string[] otherMacs) =>
        new(uuid, mac, [mac, .. otherMacs], "Microsoft Corporation", "Virtual Machine", "0000-0000", "1.0.0");

    public Task<HttpResponseMessage> RegisterAsync(AgentRegistration registration, string? bearer = null) =>
        SendAsync(HttpMethod.Post, AgentRoutes.Register, bearer, JsonContent.Create(registration, options: TestJson.Options));

    public Task<HttpResponseMessage> GetAsync(string path) => SendAsync(HttpMethod.Get, path, null, null);

    public Task<HttpResponseMessage> NextAsync(Guid machineId, string token) =>
        SendAsync(HttpMethod.Get, AgentRoutes.Next(machineId), token, null);

    public Task<HttpResponseMessage> LogAsync(Guid machineId, string token, AgentLogBatch batch) =>
        SendAsync(HttpMethod.Post, AgentRoutes.Log(machineId), token, JsonContent.Create(batch, options: TestJson.Options));

    public Task<HttpResponseMessage> SignInAsync(Guid machineId, string token, AgentSignInRequest request) =>
        SendAsync(HttpMethod.Post, AgentRoutes.SignIn(machineId), token, JsonContent.Create(request, options: TestJson.Options));

    public Task<HttpResponseMessage> ImagesAsync(Guid machineId, string token) =>
        SendAsync(HttpMethod.Get, AgentRoutes.Images(machineId), token, null);

    public Task<HttpResponseMessage> PickAsync(Guid machineId, string token, AgentPickRequest request) =>
        SendAsync(HttpMethod.Post, AgentRoutes.Deployments(machineId), token, JsonContent.Create(request, options: TestJson.Options));

    public Task<HttpResponseMessage> ReportAsync(Guid machineId, string token, Guid deploymentId, AgentDeploymentReport report) =>
        SendAsync(
            HttpMethod.Post,
            AgentRoutes.DeploymentReport(machineId, deploymentId),
            token,
            JsonContent.Create(report, options: TestJson.Options));

    public Task<HttpResponseMessage> UnattendAsync(Guid machineId, string token, Guid deploymentId) =>
        SendAsync(HttpMethod.Get, AgentRoutes.DeploymentUnattend(machineId, deploymentId), token, null);

    public Task<HttpResponseMessage> ImageAsync(
        Guid machineId,
        string token,
        string sha256,
        HttpMethod? method = null,
        RangeHeaderValue? range = null) =>
        SendAsync(method ?? HttpMethod.Get, AgentRoutes.ImageContent(machineId, sha256), token, null, request => request.Headers.Range = range);

    public void Dispose() => client.Dispose();

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        string? token,
        HttpContent? content,
        Action<HttpRequestMessage>? configure = null)
    {
        using HttpRequestMessage request = new(method, new Uri(path, UriKind.Relative)) { Content = content };

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (remoteAddress is not null)
        {
            request.Headers.Add(TestRemoteAddress.Header, remoteAddress);
        }

        configure?.Invoke(request);

        return await client.SendAsync(request);
    }
}
