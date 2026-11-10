// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
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

// The console for Linux, which the agents in the Linux boot environment and in the installed Linux show, uploaded on
// the settings page beside the console for Windows. It takes only Linux programs and libraries for x86-64.
public sealed class LinuxConsoleUploadTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private const string Settings = "/api/settings/agent/linux/console";

    private string AgentDirectory => Path.Combine(application.StorePath, "agent");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnUploadReplacesTheConsoleLinuxMachinesSwitchTo()
    {
        SignedInClient administrator = await application.AdministratorAsync();
        using HttpClient machine = application.CreateClient();
        (string Path, byte[] Content)[] files = Files("26.1.540", "ddt-console-linux-x64/");
        await using LiveListener listener = await LiveListener.StartAsync(application, administrator);
        ChannelReader<JsonElement> changes = listener.Listen<JsonElement>(LiveEvents.LinuxConsoleChanged);

        Assert.Equal(AgentBinarySource.None, (await RegisteredMachine.ReadAsync<AgentBinaryView>(await administrator.GetAsync(Settings))).Source);
        Assert.Equal(HttpStatusCode.NotFound, (await machine.GetAsync(Route(), Cancellation)).StatusCode);

        AgentBinaryView uploaded = await RegisteredMachine.ReadAsync<AgentBinaryView>(
            await UploadAsync(administrator, ConsolePackages.Zip(files), await administrator.TokenAsync()));

        string sha256 = ConsolePackages.Sha256(files[0].Content);
        Assert.Equal((sha256, files.Sum(file => (long)file.Content.Length), AgentBinarySource.Uploaded, "26.1.540"), (uploaded.Sha256, uploaded.Size, uploaded.Source, uploaded.Version));
        Assert.Equal(sha256, (await LiveListener.NextAsync(changes)).GetProperty("sha256").GetString());

        // What the agents for Linux are told to switch to, and what they download. The console for Windows stays.
        ConsoleRelease? release = await machine.GetFromJsonAsync<ConsoleRelease>(Route(), Cancellation);
        Assert.Equal(ConsoleRelease.LinuxFileNames, release!.Files.Select(file => file.Name));
        Assert.Equal(files.Select(file => ConsolePackages.Sha256(file.Content)), release.Files.Select(file => file.Sha256));

        foreach (((string _, byte[] content), ConsoleReleaseFile file) in files.Zip(release.Files))
        {
            Assert.Equal(content, await machine.GetByteArrayAsync(Route("/" + file.Name), Cancellation));
        }

        Assert.Equal(HttpStatusCode.NotFound, (await machine.GetAsync(Route("/ddt-console.exe"), Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await machine.GetAsync(new Uri("api/agents/releases/arm64/console", UriKind.Relative), Cancellation)).StatusCode);
        Assert.Equal(AgentBinarySource.None, (await RegisteredMachine.ReadAsync<AgentBinaryView>(await administrator.GetAsync("/api/settings/agent/console"))).Source);

        using (ZipArchive stored = ZipFile.OpenRead(Path.Combine(AgentDirectory, ConsoleReleaseStores.LinuxFileName)))
        {
            Assert.Equal(ConsoleRelease.LinuxFileNames, stored.Entries.Select(entry => entry.FullName));
        }

        Assert.True(await application.QueryAsync(database => database.AuditEvents.AnyAsync(
            audit => audit.Action == AuditActions.LinuxConsoleUploaded && audit.SubjectId == sha256,
            Cancellation)));

        // Removed again, machines get none from the server.
        HttpResponseMessage removed = await administrator.DeleteAsync(Settings);
        Assert.Equal(AgentBinarySource.None, (await RegisteredMachine.ReadAsync<AgentBinaryView>(removed)).Source);
        Assert.Equal(HttpStatusCode.NotFound, (await machine.GetAsync(Route(), Cancellation)).StatusCode);
    }

    public static TheoryData<string> NotLinuxConsoles => ["the console for Windows", "a library for another processor", "Windows' names"];

    [Theory]
    [MemberData(nameof(NotLinuxConsoles))]
    public async Task OnlyALinuxConsoleIsTaken(string upload)
    {
        (string Path, byte[] Content)[] files = Files("26.1.540");
        byte[] content = upload switch
        {
            "the console for Windows" => ConsolePackages.Zip([.. files[..2], ("libHarfBuzzSharp.so", ConsolePackages.Executable(2000))]),
            "a library for another processor" => ConsolePackages.Zip([.. files[..2], ("libHarfBuzzSharp.so", [.. LinuxExecutables.Header(machine: 183), .. new byte[100]])]),
            _ => ConsolePackages.Zip(ConsolePackages.Files()),
        };
        SignedInClient administrator = await application.AdministratorAsync();

        HttpResponseMessage response = await UploadAsync(administrator, content, await administrator.TokenAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("package", (await SettingsRequests.ProblemsAsync(response)).Errors.Keys);
    }

    private static Uri Route(string rest = "") => new(AgentRoutes.PlatformConsoleRelease(AgentPlatforms.LinuxX64) + rest, UriKind.Relative);

    // The console's three files as Publish-LinuxConsole.ps1 writes them: ELF files, the console's with its version.
    private static (string Path, byte[] Content)[] Files(string version, string folder = "") =>
    [
        (folder + "ddt-console", [.. LinuxExecutables.Header(), .. RandomNumberGenerator.GetBytes(5000), .. Encoding.ASCII.GetBytes($"{ElfAgentVersion.ConsoleMarker}{version}\n")]),
        (folder + "libSkiaSharp.so", [.. LinuxExecutables.Header(), .. RandomNumberGenerator.GetBytes(3000)]),
        (folder + "libHarfBuzzSharp.so", [.. LinuxExecutables.Header(), .. RandomNumberGenerator.GetBytes(2000)]),
    ];

    private static async Task<HttpResponseMessage> UploadAsync(SignedInClient client, byte[] content, string? token)
    {
        using HttpRequestMessage request = new(HttpMethod.Put, new Uri(Settings, UriKind.Relative))
        {
            Content = new ByteArrayContent(content) { Headers = { ContentType = new MediaTypeHeaderValue("application/zip") } },
        };

        if (token is not null)
        {
            request.Headers.Add(ReauthenticationTokens.HeaderName, token);
        }

        return await client.SendAsync(request, Cancellation);
    }
}
