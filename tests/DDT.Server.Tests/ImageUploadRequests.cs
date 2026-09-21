// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using DDT.Contracts.Images;
using Xunit;

namespace DDT.Server.Tests;

// The upload protocol as the SPA speaks it: create a session, send chunks at an offset, complete.
internal static class ImageUploadRequests
{
    public const string Uploads = "/api/images/uploads";
    public const string UploadOffset = "Upload-Offset";

    public static Task<HttpResponseMessage> CreateUploadAsync(
        this SignedInClient client,
        string fileName,
        long length,
        long lastModified = 1_700_000_000_000) =>
        client.PostAsync(Uploads, new CreateImageUploadRequest(fileName, length, lastModified));

    public static async Task<ImageUploadSession> StartUploadAsync(this SignedInClient client, byte[] file, string? fileName = null)
    {
        ArgumentNullException.ThrowIfNull(file);

        return await RegisteredMachine.ReadAsync<ImageUploadSession>(
            await client.CreateUploadAsync(fileName ?? $"install-{Guid.NewGuid():N}.wim", file.Length));
    }

    public static async Task<HttpResponseMessage> SendChunkAsync(this SignedInClient client, Guid uploadId, long offset, ReadOnlyMemory<byte> chunk)
    {
        using HttpRequestMessage request = new(HttpMethod.Patch, new Uri($"{Uploads}/{uploadId}", UriKind.Relative))
        {
            Content = new ByteArrayContent(chunk.ToArray()),
        };

        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Headers.Add(UploadOffset, offset.ToString(CultureInfo.InvariantCulture));

        return await client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> CompleteUploadAsync(this SignedInClient client, Guid uploadId) =>
        client.PostAsync($"{Uploads}/{uploadId}/complete");

    // A completion the client can give up on, as a browser behind a proxy's read timeout does.
    public static async Task<HttpResponseMessage> CompleteUploadOrGiveUpAsync(this SignedInClient client, Guid uploadId, CancellationToken giveUp)
    {
        ArgumentNullException.ThrowIfNull(client);

        using HttpRequestMessage request = new(HttpMethod.Post, new Uri($"{Uploads}/{uploadId}/complete", UriKind.Relative));

        return await client.SendAsync(request, giveUp);
    }

    // Sends the whole file in chunks of the given size, checking that each one is taken.
    public static async Task<ImageUploadSession> UploadAsync(this SignedInClient client, byte[] file, int chunkBytes = 1500, string? fileName = null)
    {
        ArgumentNullException.ThrowIfNull(file);

        ImageUploadSession session = await client.StartUploadAsync(file, fileName);

        for (int offset = 0; offset < file.Length; offset += chunkBytes)
        {
            int length = Math.Min(chunkBytes, file.Length - offset);
            using HttpResponseMessage response = await client.SendChunkAsync(session.Id, offset, file.AsMemory(offset, length));

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal(offset + length, OffsetOf(response));
        }

        return session;
    }

    public static long OffsetOf(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return long.Parse(response.Headers.GetValues(UploadOffset).Single(), CultureInfo.InvariantCulture);
    }
}
