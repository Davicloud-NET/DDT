// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Channels;
using DDT.Contracts.BootImage;
using DDT.Pxe;
using DDT.Server.Authentication;
using DDT.Server.BootImage;
using DDT.Server.Certificates;
using DDT.Server.Live;
using DDT.Server.Machines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static DDT.Server.Tests.AccountRequests;

namespace DDT.Server.Tests;

// The Build button, with a helper that is a test's own. What the helper itself does is in HelperBootImageBuildTests.
public sealed class BootImageBuildTests : IDisposable
{
    private const string BootImage = "/api/boot-image";

    private readonly BootImageBuildApplication _application = new();

    private BootImageCatalog Catalog => _application.Services.GetRequiredService<BootImageCatalog>();

    [Fact]
    public async Task AnAdministratorBuildsAndThePageFollowsTheJob()
    {
        SignedInClient administrator = await _application.AdministratorAsync();
        await using LiveListener live = await LiveListener.StartAsync(_application, administrator);
        ChannelReader<BootImageJobOutput> output = live.Listen<BootImageJobOutput>(LiveEvents.BootImageJobOutput);
        _application.Helper.OnDone = request => WriteBuild(request.Name ?? string.Empty, "2026-10-01T10:00:00Z", current: true);

        using HttpResponseMessage started = await administrator.PostAsync($"{BootImage}/build", new BuildBootImageRequest("0407:00000407", SkipPowerShell: true, TftpWindowSize: 8));
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        await _application.Services.GetRequiredService<BootImageJobs>().Last;

        HelperRequest request = Assert.Single(_application.Helper.Requests);
        Assert.Equal(HelperRequest.Build, request.Kind);
        Assert.Matches("^[0-9]{8}-[0-9]{6}$", request.Name);
        Assert.Equal($"https://{ServerNames.DnsName()}:8443", request.ServerUrl, ignoreCase: true);
        Assert.Equal(request.ServerUrl, (await ViewAsync(administrator)).Builder.ServerUrl);
        Assert.Equal(("0407:00000407", true, 8), (request.KeyboardLayout, request.SkipPowerShell, request.TftpWindowSize));

        Assert.Equal(["Copying Windows PE", "Adding the agent"], (await LiveListener.NextAsync(output)).Lines);

        BootImageJobLog log = await RegisteredMachine.ReadAsync<BootImageJobLog>(await administrator.GetAsync($"{BootImage}/job"));
        Assert.Equal((BootImageJobKind.Build, BootImageJobState.Succeeded, 2), (log.Job.Kind, log.Job.State, log.Job.Lines));
        Assert.Equal(_application.Helper.Lines, log.Lines);

        BootImageView view = await ViewAsync(administrator);
        Assert.Equal(BootImageJobState.Succeeded, view.Job?.State);
        Assert.Equal(request.Name, Assert.Single(view.Builds).Name);
        Assert.True(view.Builds[0].Current);
        Assert.NotNull(view.Build);
        Assert.Equal(1, await _application.QueryAsync(database =>
            database.AuditEvents.CountAsync(audit => audit.Action == AuditActions.BootImageBuildStarted && audit.SubjectId == request.Name, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task WhileAJobRunsAnotherIsRefusedAndAFailedOneSaysWhy()
    {
        SignedInClient administrator = await _application.AdministratorAsync();
        _application.Helper.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _application.Helper.Problem = "Build-BootImage.ps1 ended with exit code 1. Its last lines say why.";

        Assert.Equal(HttpStatusCode.Accepted, (await administrator.PostAsync($"{BootImage}/build", new BuildBootImageRequest(null))).StatusCode);

        using HttpResponseMessage second = await administrator.PostAsync($"{BootImage}/adk");
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("bootImage.busy", await CodeAsync(second));
        Assert.Equal(BootImageJobState.Running, (await ViewAsync(administrator)).Job?.State);

        _application.Helper.Gate.SetResult();
        await _application.Services.GetRequiredService<BootImageJobs>().Last;

        BootImageJob? job = (await ViewAsync(administrator)).Job;
        Assert.Equal(BootImageJobState.Failed, job?.State);
        Assert.Equal(_application.Helper.Problem, job?.Problem);
        Assert.NotNull(job?.FinishedUtc);
    }

    [Fact]
    public async Task WithoutAHelperOrTheAdkNothingStartsAndTheAdkCanBeInstalled()
    {
        SignedInClient administrator = await _application.AdministratorAsync();
        _application.Adk = new BootImageAdk(false, null, false);

        using HttpResponseMessage noAdk = await administrator.PostAsync($"{BootImage}/build", new BuildBootImageRequest(null));
        Assert.Equal("bootImage.noAdk", await CodeAsync(noAdk));

        Assert.Equal(HttpStatusCode.Accepted, (await administrator.PostAsync($"{BootImage}/adk")).StatusCode);
        await _application.Services.GetRequiredService<BootImageJobs>().Last;
        Assert.Equal(HelperRequest.InstallAdk, Assert.Single(_application.Helper.Requests).Kind);
        Assert.Equal(BootImageJobKind.InstallAdk, (await ViewAsync(administrator)).Job?.Kind);

        _application.Adk = new BootImageAdk(true, "10.1.22621.1", false);
        using HttpResponseMessage oldAdk = await administrator.PostAsync($"{BootImage}/build", new BuildBootImageRequest(null));
        Assert.Equal("bootImage.oldAdk", await CodeAsync(oldAdk));

        _application.Helper.Available = false;
        using HttpResponseMessage noHelper = await administrator.PostAsync($"{BootImage}/build", new BuildBootImageRequest(null));
        Assert.Equal(HttpStatusCode.Conflict, noHelper.StatusCode);
        Assert.Equal("bootImage.noHelper", await CodeAsync(noHelper));
        Assert.False((await ViewAsync(administrator)).Builder.Available);
    }

    [Fact]
    public async Task OnlyAnAdministratorBuildsAndOnlyWithValuesThatFit()
    {
        using SignedInClient operatorClient = await _application.SignInAsync(DdtRoleNames.Operator);
        SignedInClient administrator = await _application.AdministratorAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.PostAsync($"{BootImage}/build", new BuildBootImageRequest(null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.PostAsync($"{BootImage}/adk")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.PostAsync($"{BootImage}/current", new UseBootImageBuildRequest(null))).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await administrator.PostAsync($"{BootImage}/build", new BuildBootImageRequest("German"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await administrator.PostAsync($"{BootImage}/build", new BuildBootImageRequest(null, TftpWindowSize: 65))).StatusCode);
        Assert.Empty(_application.Helper.Requests);
        Assert.Equal(HttpStatusCode.NoContent, (await operatorClient.GetAsync($"{BootImage}/job")).StatusCode);
    }

    [Fact]
    public async Task GoingBackServesTheBuildBeforeAndOldBuildsGoAfterANewOne()
    {
        SignedInClient administrator = await _application.AdministratorAsync();
        WriteBuild("20260901-080000", "2026-09-01T08:00:00Z");
        WriteBuild("20260915-080000", "2026-09-15T08:00:00Z");
        BootBuilds.SetCurrent(Catalog.BootDirectory, "20260915-080000");

        BootImageView before = await ViewAsync(administrator);
        Assert.Equal(["20260915-080000", "20260901-080000"], before.Builds.Select(build => build.Name));
        Assert.Equal([true, false], before.Builds.Select(build => build.Current));

        BootImageView back = await RegisteredMachine.ReadAsync<BootImageView>(
            await administrator.PostAsync($"{BootImage}/current", new UseBootImageBuildRequest("20260901-080000")));
        Assert.Equal([false, true], back.Builds.Select(build => build.Current));
        Assert.EndsWith(Path.Combine("builds", "20260901-080000"), BootBuilds.Serving(Catalog.BootDirectory), StringComparison.Ordinal);

        using HttpResponseMessage unknown = await administrator.PostAsync($"{BootImage}/current", new UseBootImageBuildRequest("20200101-000000"));
        Assert.Equal("bootImage.noSuchBuild", await CodeAsync(unknown));

        _application.Helper.OnDone = request => WriteBuild(request.Name ?? string.Empty, "2026-10-01T10:00:00Z", current: true);
        (await administrator.PostAsync($"{BootImage}/build", new BuildBootImageRequest(null))).EnsureSuccessStatusCode();
        await _application.Services.GetRequiredService<BootImageJobs>().Last;

        // The new build and the one that was served before it stay
        Assert.Equal(2, BootBuilds.List(Catalog.BootDirectory).Count);
        Assert.Contains("20260901-080000", BootBuilds.List(Catalog.BootDirectory));
    }

    [Fact]
    public async Task ABuildMadeForAnotherAddressRootOrAdkIsDueAgain()
    {
        SignedInClient administrator = await _application.AdministratorAsync();
        using X509Certificate2 root = X509Certificate2.CreateFromPem(await File.ReadAllTextAsync(_application.Files.RootPath, TestContext.Current.CancellationToken));
        string rootSha256 = Convert.ToHexString(SHA256.HashData(root.RawData));

        WriteBuild("20261001-100000", "2026-10-01T10:00:00Z", current: true, ($"https://{ServerNames.DnsName()}:8443", rootSha256, "10.1.26100.9457"));
        BootImageView fits = await ViewAsync(administrator);
        Assert.False(fits.Stale);
        Assert.Empty(fits.StaleReasons);

        WriteBuild("20261001-110000", "2026-10-01T11:00:00Z", current: true, ("https://old-name.example:9443", new string('A', 64), "10.1.26100.2454"));
        BootImageView due = await ViewAsync(administrator);
        Assert.True(due.Stale);
        Assert.Equal([BootImageViews.ServerAddressChanged, BootImageViews.RootChanged, BootImageViews.AdkChanged], due.StaleReasons);
    }

    public void Dispose() => _application.Dispose();

    private static async Task<BootImageView> ViewAsync(SignedInClient client) =>
        await RegisteredMachine.ReadAsync<BootImageView>(await client.GetAsync(BootImage));

    // A build as Build-BootImage.ps1 leaves it: its files and the description next to boot.wim.
    private void WriteBuild(string name, string builtUtc, bool current = false, (string ServerUrl, string RootSha256, string AdkVersion)? madeFor = null)
    {
        string boot = Path.Combine(BootBuilds.FolderOf(Catalog.BootDirectory, name), "Boot");
        Directory.CreateDirectory(boot);
        File.WriteAllText(Path.Combine(boot, "boot.wim"), name);
        File.WriteAllText(Path.Combine(boot, BootImageCatalog.ManifestName), $$"""
            {
              "builtUtc": "{{builtUtc}}",
              "drivers": [],
              "adkVersion": {{Json(madeFor?.AdkVersion)}},
              "serverUrl": {{Json(madeFor?.ServerUrl)}},
              "rootSha256": {{Json(madeFor?.RootSha256)}}
            }
            """);

        if (current)
        {
            BootBuilds.SetCurrent(Catalog.BootDirectory, name);
        }
    }

    private static string Json(string? value) => value is null ? "null" : $"\"{value}\"";
}
