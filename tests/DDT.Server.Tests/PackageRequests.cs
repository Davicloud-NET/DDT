// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Images;
using DDT.Contracts.Packages;

namespace DDT.Server.Tests;

// Packages go through the image upload with a kind, as the SPA sends them.
internal static class PackageRequests
{
    public const string Packages = "/api/packages";

    public static async Task<ImageUploadSession> StartPackageUploadAsync(
        this SignedInClient client,
        byte[] zip,
        UploadKind kind,
        string? fileName = null,
        long lastModified = 1_700_000_000_000)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(zip);

        return await RegisteredMachine.ReadAsync<ImageUploadSession>(await client.PostAsync(
            ImageUploadRequests.Uploads,
            new CreateImageUploadRequest(fileName ?? $"drivers-{Guid.NewGuid():N}.zip", zip.Length, lastModified, kind)));
    }

    // Sends the whole zip in one chunk, then completes the upload and returns that answer.
    public static async Task<HttpResponseMessage> UploadPackageAsync(this SignedInClient client, byte[] zip, UploadKind kind, string? fileName = null)
    {
        ImageUploadSession session = await client.StartPackageUploadAsync(zip, kind, fileName);
        (await client.SendChunkAsync(session.Id, 0, zip)).EnsureSuccessStatusCode();

        return await client.CompleteUploadAsync(session.Id);
    }

    public static async Task<PackageSummary> UploadedPackageAsync(this SignedInClient client, byte[] zip, UploadKind kind, string? fileName = null) =>
        await RegisteredMachine.ReadAsync<PackageSummary>(await client.UploadPackageAsync(zip, kind, fileName));

    public static byte[] DriverZip() => TestZip.Create("Audio/HDX.inf", "Audio/hdx.sys");
}
