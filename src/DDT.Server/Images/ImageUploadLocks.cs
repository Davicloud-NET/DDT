// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace DDT.Server.Images;

// One lock per upload session, shared by every request and the sweeper, and never waited for: a caller that finds
// it held answers busy or skips the session. In process only, which holds because DDT runs as one web instance.
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
