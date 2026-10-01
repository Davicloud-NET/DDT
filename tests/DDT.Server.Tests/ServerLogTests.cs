// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Contracts.Server;
using DDT.Server.Settings;
using Xunit;

namespace DDT.Server.Tests;

// The Server page lists the log files a Windows service writes into the store, and downloads one.
public sealed class ServerLogTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    private const string Logs = "/api/server/logs";

    [Fact]
    public async Task AnAdministratorListsTheLogFilesNewestFirstAndDownloadsOne()
    {
        string folder = LogFiles.FolderIn(application.StorePath);
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "ddt-20260930.log"), "yesterday\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(folder, "ddt-20261001.log"), "today\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(folder, "notes.txt"), "not a log", TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(Path.Combine(folder, "ddt-20260930.log"), DateTime.UtcNow.AddDays(-1));
        SignedInClient administrator = await application.AdministratorAsync();

        IReadOnlyList<LogFileView> files = await RegisteredMachine.ReadAsync<IReadOnlyList<LogFileView>>(await administrator.GetAsync(Logs));

        Assert.Equal(["ddt-20261001.log", "ddt-20260930.log"], files.Select(file => file.Name));
        Assert.Equal(6, files[0].Size);

        // While the server may still be writing it
        await using FileStream writing = new(Path.Combine(folder, "ddt-20261001.log"), FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        HttpResponseMessage download = await administrator.GetAsync(Logs + "/ddt-20261001.log");

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("text/plain", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal("ddt-20261001.log", download.Content.Headers.ContentDisposition?.FileName);
        Assert.Equal("today\n", await download.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("ddt-19990101.log")]
    [InlineData("..%2Fddt-dev.db")]
    [InlineData("..%5Cfirst-admin.txt")]
    public async Task OnlyALogFileIsHandedOut(string name)
    {
        Directory.CreateDirectory(LogFiles.FolderIn(application.StorePath));
        await File.WriteAllTextAsync(Path.Combine(LogFiles.FolderIn(application.StorePath), "notes.txt"), "not a log", TestContext.Current.CancellationToken);
        SignedInClient administrator = await application.AdministratorAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await administrator.GetAsync($"{Logs}/{name}")).StatusCode);
    }

    [Fact]
    public async Task OnlyAnAdministratorReadsTheLogs()
    {
        SignedInClient @operator = await application.SignInAsync(DDT.Server.Authentication.DdtRoleNames.Operator);

        Assert.Equal(HttpStatusCode.Forbidden, (await @operator.GetAsync(Logs)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await @operator.GetAsync(Logs + "/ddt-20261001.log")).StatusCode);
    }
}
