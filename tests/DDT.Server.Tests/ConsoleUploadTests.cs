// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Channels;
using DDT.Contracts.Agents;
using DDT.Contracts.Settings;
using DDT.Server.Live;
using DDT.Server.Machines;
using DDT.Server.Settings;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DDT.Server.Tests;

// The console the agents of netbooting machines switch to, uploaded on the settings page like the agent: only with a
// fresh proof of identity, only a zip of the console's three files, and not while configuration names it.
public sealed class ConsoleUploadTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private string AgentDirectory => Path.Combine(application.StorePath, "agent");

    // Zipping the folder Publish-Console.ps1 writes puts the files in a folder, which the server takes as well.
    [Fact]
    public async Task AnUploadReplacesTheConsoleMachinesSwitchTo()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        SignedInClient administrator = await application.AdministratorAsync();
        (string Path, byte[] Content)[] files = ConsolePackages.Files("console/");
        await using LiveListener listener = await LiveListener.StartAsync(application, administrator);
        ChannelReader<JsonElement> changes = listener.Listen<JsonElement>(LiveEvents.ConsoleChanged);

        Assert.Equal(AgentBinarySource.None, (await RegisteredMachine.ReadAsync<AgentBinaryView>(await administrator.GetAsync("/api/settings/agent/console"))).Source);

        AgentBinaryView uploaded = await RegisteredMachine.ReadAsync<AgentBinaryView>(
            await UploadAsync(administrator, ConsolePackages.Zip(files), await administrator.TokenAsync()));

        string sha256 = ConsolePackages.Sha256(files[0].Content);
        Assert.Equal(sha256, uploaded.Sha256);
        Assert.Equal(files.Sum(file => file.Content.Length), uploaded.Size);
        Assert.Equal(AgentBinarySource.Uploaded, uploaded.Source);
        Assert.NotNull(uploaded.UploadedBy);

        AgentBinaryView view = await RegisteredMachine.ReadAsync<AgentBinaryView>(await administrator.GetAsync("/api/settings/agent/console"));
        Assert.Equal((sha256, uploaded.Size, AgentBinarySource.Uploaded, uploaded.UploadedBy), (view.Sha256, view.Size, view.Source, view.UploadedBy));
        Assert.NotNull(view.UploadedUtc);

        JsonElement pushed = await LiveListener.NextAsync(changes);
        Assert.Equal(sha256, pushed.GetProperty("sha256").GetString());

        // What agents are told to switch to, and what they download, file by file.
        using AgentClient agent = new(application.CreateDefaultClient(), TestRemoteAddress.Unique());
        ConsoleRelease release = await RegisteredMachine.ReadAsync<ConsoleRelease>(await agent.GetAsync(AgentRoutes.ConsoleRelease));

        Assert.Equal(ConsoleRelease.FileNames, release.Files.Select(file => file.Name));
        Assert.Equal(files.Select(file => ConsolePackages.Sha256(file.Content)), release.Files.Select(file => file.Sha256));
        Assert.Equal(files.Select(file => (long)file.Content.Length), release.Files.Select(file => file.Size));

        foreach (((string _, byte[] content), ConsoleReleaseFile file) in files.Zip(release.Files))
        {
            HttpResponseMessage download = await agent.GetAsync(AgentRoutes.ConsoleReleaseFile(file.Name));

            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
            Assert.Equal(content.Length, download.Content.Headers.ContentLength);
            Assert.Equal(content, await download.Content.ReadAsByteArrayAsync(cancellationToken));
        }

        Assert.Equal(HttpStatusCode.NotFound, (await agent.GetAsync(AgentRoutes.ConsoleReleaseFile("ddt-agent.exe"))).StatusCode);

        // Stored with the files at the root and nothing else.
        using (ZipArchive stored = ZipFile.OpenRead(Path.Combine(AgentDirectory, "ddt-console.zip")))
        {
            Assert.Equal(ConsoleRelease.FileNames, stored.Entries.Select(entry => entry.FullName));
        }

        Assert.True(await application.QueryAsync(database => database.AuditEvents.AnyAsync(
            audit => audit.Action == AuditActions.ConsoleUploaded && audit.SubjectId == sha256,
            cancellationToken)));
        Assert.Empty(Directory.GetFiles(AgentDirectory, "*.upload"));
    }

    public static TheoryData<string> NotConsoles => ["not a zip", "a file missing", "another file", "two folders", "a file that is not a program"];

    [Theory]
    [MemberData(nameof(NotConsoles))]
    public async Task OnlyTheConsolesFilesAreTaken(string upload)
    {
        (string Path, byte[] Content)[] files = ConsolePackages.Files();
        byte[] content = upload switch
        {
            "not a zip" => ConsolePackages.Executable(4096),
            "a file missing" => ConsolePackages.Zip(files[..2]),
            "another file" => ConsolePackages.Zip([.. files, ("ddt-agent.exe", ConsolePackages.Executable(100))]),
            "two folders" => ConsolePackages.Zip([.. ConsolePackages.Files("a/")[..2], ("b/libHarfBuzzSharp.dll", files[2].Content)]),
            _ => ConsolePackages.Zip([.. files[..2], ("libHarfBuzzSharp.dll", "#!/bin/sh\n"u8.ToArray())]),
        };
        SignedInClient administrator = await application.AdministratorAsync();
        string? before = (await RegisteredMachine.ReadAsync<AgentBinaryView>(await administrator.GetAsync("/api/settings/agent/console"))).Sha256;

        HttpResponseMessage response = await UploadAsync(administrator, content, await administrator.TokenAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("package", (await SettingsRequests.ProblemsAsync(response)).Errors.Keys);
        Assert.Equal(before, (await RegisteredMachine.ReadAsync<AgentBinaryView>(await administrator.GetAsync("/api/settings/agent/console"))).Sha256);
        Assert.Empty(Directory.Exists(AgentDirectory) ? Directory.GetFiles(AgentDirectory, "*.upload") : []);
    }

    [Fact]
    public async Task AnUploadNeedsAFreshProofOfIdentity()
    {
        SignedInClient administrator = await application.AdministratorAsync();

        HttpResponseMessage response = await UploadAsync(administrator, ConsolePackages.Zip(ConsolePackages.Files()), null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task OnlyAnAdministratorUploads()
    {
        SignedInClient @operator = await application.SignInAsync(DDT.Server.Authentication.DdtRoleNames.Operator);

        Assert.Equal(HttpStatusCode.Forbidden, (await @operator.GetAsync("/api/settings/agent/console")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(@operator, ConsolePackages.Zip(ConsolePackages.Files()), null)).StatusCode);
    }

    // DDT:Agent:ConsolePath is a development override, and the page cannot replace a file configuration names.
    [Fact]
    public async Task AConfiguredConsoleCannotBeReplaced()
    {
        using AgentReleaseApplication configured = new();
        SignedInClient administrator = await configured.AdministratorAsync();

        HttpResponseMessage response = await UploadAsync(administrator, ConsolePackages.Zip(ConsolePackages.Files()), await administrator.TokenAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(
            AgentBinarySource.Configuration,
            (await RegisteredMachine.ReadAsync<AgentBinaryView>(await administrator.GetAsync("/api/settings/agent/console"))).Source);
    }

    private static async Task<HttpResponseMessage> UploadAsync(SignedInClient client, byte[] content, string? token)
    {
        using HttpRequestMessage request = new(HttpMethod.Put, new Uri("/api/settings/agent/console", UriKind.Relative))
        {
            Content = new ByteArrayContent(content) { Headers = { ContentType = new MediaTypeHeaderValue("application/zip") } },
        };

        if (token is not null)
        {
            request.Headers.Add(ReauthenticationTokens.HeaderName, token);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
