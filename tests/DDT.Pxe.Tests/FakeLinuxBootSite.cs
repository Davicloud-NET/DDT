// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Pxe.Tests;

// The server's side of the Linux boot environment, as a test sets it. Failure is thrown by every question.
internal sealed class FakeLinuxBootSite : ILinuxBootSite
{
    private LinuxBootBundle? _current;
    private int _refreshed;

    public LinuxBootBundle? Current
    {
        get => Failure is null ? _current : throw Failure;
        set => _current = value;
    }

    public Exception? Failure { get; set; }

    // How often netboot looked at the files again.
    public int Refreshed => Volatile.Read(ref _refreshed);

    public void Refresh()
    {
        Interlocked.Increment(ref _refreshed);

        if (Failure is not null)
        {
            throw Failure;
        }
    }
}
