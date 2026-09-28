// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent;

// The body of a download, such as an image or a package. It starts at Offset in a file that is TotalLength bytes
// long. Disposing it ends the response and frees the connection.
public sealed class AgentImageStream(Stream content, long offset, long totalLength, IDisposable? response = null) : IAsyncDisposable
{
    public Stream Content { get; } = content;

    public long Offset { get; } = offset;

    public long TotalLength { get; } = totalLength;

    public async ValueTask DisposeAsync()
    {
        await Content.DisposeAsync().ConfigureAwait(false);
        response?.Dispose();
    }
}
