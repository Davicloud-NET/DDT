// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Security.Cryptography;

namespace DDT.Agent.Deployment;

// Where the bytes of a download go. Length is how many it holds, which is where a resumed download goes on.
public interface IDownloadSink
{
    long Length { get; }

    // Adds what it holds from an earlier attempt to hash.
    Task HashExistingAsync(IncrementalHash hash, CancellationToken cancellationToken);

    Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken);

    // Back to holding nothing, as when the server sends the whole file again.
    void Restart();
}
