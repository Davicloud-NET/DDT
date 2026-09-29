// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace DDT.Server.Images;

// Keeps one lock per upload session, shared by every request and the sweeper. Nobody waits for a lock. A caller that
// finds it held answers busy or skips the session. The locks only work within one process. That's fine because DDT
// runs as a single web instance.
public sealed class ImageUploadLocks
{
    private readonly ConcurrentDictionary<Guid, ImageUploadLock> _held = new();

    public bool TryEnter(Guid uploadId, [NotNullWhen(true)] out ImageUploadLock? held)
    {
        ImageUploadLock candidate = new(this, uploadId);

        if (_held.TryAdd(uploadId, candidate))
        {
            held = candidate;

            return true;
        }

        held = null;

        return false;
    }

    internal void Exit(ImageUploadLock held) => _held.TryRemove(new KeyValuePair<Guid, ImageUploadLock>(held.UploadId, held));
}
