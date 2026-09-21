// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Images;

// Released by disposing it, exactly once however often that happens.
public sealed class ImageUploadLock : IDisposable
{
    private readonly ImageUploadLocks _owner;
    private int _released;

    internal ImageUploadLock(ImageUploadLocks owner, Guid uploadId)
    {
        _owner = owner;
        UploadId = uploadId;
    }

    public Guid UploadId { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
        {
            _owner.Exit(this);
        }
    }
}
