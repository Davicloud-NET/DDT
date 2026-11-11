// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Applications;
using DDT.Contracts.Images;

namespace DDT.Server.Tests;

// The application library as the SPA uses it.
internal static class ApplicationRequests
{
    public static async Task<ApplicationUpload> UploadedApplicationAsync(this SignedInClient client, byte[] file, string fileName)
    {
        HttpResponseMessage response = await client.UploadPackageAsync(file, UploadKind.Application, fileName);

        return await RegisteredMachine.ReadAsync<ApplicationUpload>(response);
    }
}
