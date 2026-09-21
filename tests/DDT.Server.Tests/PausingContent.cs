// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;

namespace DDT.Server.Tests;

// Sends the body in parts of partBytes and waits for the test after each part, like a slow connection that keeps
// carrying data. After the last pause it sends the rest.
internal sealed class PausingContent(byte[] content, int partBytes, IReadOnlyList<Task> resumes) : HttpContent
{
    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        int sent = 0;

        foreach (Task resume in resumes)
        {
            await stream.WriteAsync(content.AsMemory(sent, partBytes));
            await stream.FlushAsync();
            sent += partBytes;
            await resume;
        }

        await stream.WriteAsync(content.AsMemory(sent));
        await stream.FlushAsync();
    }

    protected override bool TryComputeLength(out long length)
    {
        length = content.Length;

        return true;
    }
}
