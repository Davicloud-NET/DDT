// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using DDT.Contracts.Images;
using DDT.Server.Images;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DDT.Server.Tests;

// Images and packages share one resumable upload, told apart by its kind.
public sealed class UploadKindTests(DdtApplication application) : IClassFixture<DdtApplication>
{
    [Fact]
    public async Task AnUploadWithoutAKindIsAnImage()
    {
        SignedInClient administrator = await application.AdministratorAsync();

        ImageUploadSession session = await administrator.StartUploadAsync(TestWim.Create(TestWim.X64));
        ImageUpload stored = await application.QueryAsync(database =>
            database.ImageUploads.AsNoTracking().SingleAsync(u => u.Id == session.Id, TestContext.Current.CancellationToken));

        Assert.Equal(UploadKind.Image, session.Kind);
        Assert.Equal(UploadKind.Image, stored.Kind);
        Assert.Contains(session, await RegisteredMachine.ReadAsync<IReadOnlyList<ImageUploadSession>>(await administrator.GetAsync(ImageUploadRequests.Uploads)));
    }

    [Fact]
    public async Task RefusesAKindItDoesNotKnow()
    {
        SignedInClient administrator = await application.AdministratorAsync();

        HttpResponseMessage response = await administrator.SendJsonAsync(
            HttpMethod.Post,
            ImageUploadRequests.Uploads,
            """{"fileName":"drivers.zip","length":10,"lastModified":1,"kind":"Firmware"}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
