// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;

namespace DDT.Server.Tests;

// Announces the whole body but sends only its first bytes, like a connection that stopped carrying data. It ends,
// without sending the rest, once the test lets it.
internal sealed class StallingContent(byte[] content, int sentBytes, Task release) : HttpContent
{
    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        await stream.WriteAsync(content.AsMemory(0, sentBytes));
        await stream.FlushAsync();
        await release;
    }

    protected override bool TryComputeLength(out long length)
    {
        length = content.Length;

        return true;
    }
}
