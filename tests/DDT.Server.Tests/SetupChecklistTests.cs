// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Contracts.Images;
using DDT.Contracts.Server;
using DDT.Server.Authentication;
using DDT.Server.BootImage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DDT.Server.Tests;

// What a new server still needs before its first deployment, as the first page's checklist asks.
public sealed class SetupChecklistTests : IDisposable
{
    private const string Checklist = "/api/server/checklist";

    private readonly BootImageBuildApplication _application = new();

    [Fact]
    public async Task EachStepIsDoneOnceTheServerSeesIt()
    {
        SignedInClient administrator = await _application.AdministratorAsync();
        using SignedInClient operatorClient = await _application.SignInAsync(DdtRoleNames.Operator);

        Assert.Equal(new SetupChecklist(false, false, false, false, false, false), await ReadAsync(administrator));
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.GetAsync(Checklist)).StatusCode);

        // The first password's file goes when the password is changed
        _application.Services.GetRequiredService<FirstAdministratorFile>().Delete();

        ImageUploadSession upload = await administrator.UploadAsync(TestWim.Create(TestWim.X64));
        (await administrator.CompleteUploadAsync(upload.Id)).EnsureSuccessStatusCode();

        string boot = Path.Combine(_application.Services.GetRequiredService<BootImageCatalog>().BootDirectory, "Boot");
        Directory.CreateDirectory(boot);
        await File.WriteAllTextAsync(Path.Combine(boot, "boot.wim"), "an image", TestContext.Current.CancellationToken);

        Assert.Equal(new SetupChecklist(true, false, true, true, false, false), await ReadAsync(administrator));
    }

    public void Dispose() => _application.Dispose();

    private static async Task<SetupChecklist> ReadAsync(SignedInClient client) =>
        await RegisteredMachine.ReadAsync<SetupChecklist>(await client.GetAsync(Checklist));
}
