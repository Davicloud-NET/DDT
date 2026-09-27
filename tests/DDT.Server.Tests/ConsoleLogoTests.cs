// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using DDT.Contracts.Agents;
using DDT.Contracts.Settings;
using DDT.Server.Live;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DDT.Server.Tests;

// The logo the console at the machine shows: administrators upload a PNG, operators see it, and every registration
// names its hash, so the agent downloads it anonymously like the agent and the console.
public sealed class ConsoleLogoTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    // A transparent pixel, 1 by 1.
    internal static readonly byte[] Pixel = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    // The pixel, claiming another size in its header.
    private static byte[] Sized(int width, int height)
    {
        byte[] png = [.. Pixel];
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(16), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(20), (uint)height);

        return png;
    }

    [Fact]
    public async Task AnUploadedLogoReachesTheAgentsAtTheirRegistration()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        SignedInClient administrator = await application.AdministratorAsync();
        await using LiveListener listener = await LiveListener.StartAsync(application, administrator);
        ChannelReader<JsonElement> changes = listener.Listen<JsonElement>(LiveEvents.ConsoleLogoChanged);
        string sha256 = Convert.ToHexStringLower(SHA256.HashData(Pixel));

        ConsoleLogoView uploaded = await RegisteredMachine.ReadAsync<ConsoleLogoView>(await UploadAsync(administrator, Pixel));

        Assert.Equal((sha256, (long?)Pixel.Length, (int?)1, (int?)1), (uploaded.Sha256, uploaded.Size, uploaded.Width, uploaded.Height));
        Assert.NotNull(uploaded.UploadedBy);
        Assert.NotNull(uploaded.UploadedUtc);
        Assert.Equal(sha256, (await LiveListener.NextAsync(changes)).GetProperty("sha256").GetString());

        // Operators read the page it is on.
        SignedInClient @operator = await application.SignInAsync(DDT.Server.Authentication.DdtRoleNames.Operator);
        Assert.Equal(sha256, (await RegisteredMachine.ReadAsync<ConsoleLogoView>(await @operator.GetAsync("/api/settings/console-logo"))).Sha256);
        HttpResponseMessage image = await @operator.GetAsync("/api/settings/console-logo/image");
        Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Pixel, await image.Content.ReadAsByteArrayAsync(cancellationToken));

        using AgentClient agent = new(application.CreateDefaultClient(), TestRemoteAddress.Unique());
        using HttpResponseMessage registered = await agent.RegisterAsync(AgentClient.Registration(Guid.NewGuid().ToString("D"), "02DD0000109A"));
        Assert.Equal(sha256, (await RegisteredMachine.ReadAsync<AgentRegistrationResult>(registered)).ConsoleLogoSha256);
        Assert.Equal(Pixel, await (await agent.GetAsync(AgentRoutes.ConsoleLogo)).Content.ReadAsByteArrayAsync(cancellationToken));

        Assert.True(await application.QueryAsync(database => database.AuditEvents.AnyAsync(
            audit => audit.Action == AuditActions.ConsoleLogoUploaded && audit.SubjectId == sha256,
            cancellationToken)));

        // Removed, machines show none from their next registration.
        ConsoleLogoView removed = await RegisteredMachine.ReadAsync<ConsoleLogoView>(await administrator.DeleteAsync("/api/settings/console-logo"));
        Assert.Null(removed.Sha256);
        Assert.Null((await LiveListener.NextAsync(changes)).GetProperty("sha256").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await agent.GetAsync(AgentRoutes.ConsoleLogo)).StatusCode);

        using HttpResponseMessage again = await agent.RegisterAsync(AgentClient.Registration(Guid.NewGuid().ToString("D"), "02DD0000109B"));
        Assert.Null((await RegisteredMachine.ReadAsync<AgentRegistrationResult>(again)).ConsoleLogoSha256);
        Assert.True(await application.QueryAsync(database => database.AuditEvents.AnyAsync(
            audit => audit.Action == AuditActions.ConsoleLogoRemoved && audit.SubjectId == sha256,
            cancellationToken)));
        Assert.Empty(Directory.GetFiles(Path.Combine(application.StorePath, "console"), "*.upload"));
    }

    public static TheoryData<string> NotLogos => ["a JPEG", "a cut off PNG", "a PNG without IEND", "an empty picture"];

    [Theory]
    [MemberData(nameof(NotLogos))]
    public async Task OnlyAPngIsTaken(string upload)
    {
        byte[] content = upload switch
        {
            "a JPEG" => [0xFF, 0xD8, 0xFF, 0xE0, .. RandomNumberGenerator.GetBytes(100)],
            "a cut off PNG" => Pixel[..40],
            "a PNG without IEND" => Pixel[..^12],
            _ => Sized(0, 1),
        };
        SignedInClient administrator = await application.AdministratorAsync();

        HttpResponseMessage response = await UploadAsync(administrator, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("logo", (await SettingsRequests.ProblemsAsync(response)).Errors.Keys);
    }

    [Fact]
    public async Task ALogoHasALimitedSize()
    {
        SignedInClient administrator = await application.AdministratorAsync();

        HttpResponseMessage wide = await UploadAsync(administrator, Sized(ConsoleLogoStore.MaxDimension + 1, 64));
        Assert.Equal(HttpStatusCode.BadRequest, wide.StatusCode);
        Assert.Contains("2049 by 64 pixels", await wide.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);

        HttpResponseMessage heavy = await UploadAsync(administrator, [.. Pixel, .. new byte[ConsoleLogoStore.MaxBytes]]);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, heavy.StatusCode);
    }

    [Fact]
    public async Task OnlyAnAdministratorChangesTheLogo()
    {
        SignedInClient @operator = await application.SignInAsync(DDT.Server.Authentication.DdtRoleNames.Operator);

        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(@operator, Pixel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await @operator.DeleteAsync("/api/settings/console-logo")).StatusCode);
    }

    private static async Task<HttpResponseMessage> UploadAsync(SignedInClient client, byte[] content)
    {
        using HttpRequestMessage request = new(HttpMethod.Put, new Uri("/api/settings/console-logo", UriKind.Relative))
        {
            Content = new ByteArrayContent(content) { Headers = { ContentType = new MediaTypeHeaderValue("image/png") } },
        };

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
