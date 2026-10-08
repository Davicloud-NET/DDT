// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Pxe.Tests;

// The server's side of the Linux boot environment, as a test sets it.
internal sealed class FakeLinuxBootSite : ILinuxBootSite
{
    public LinuxBootBundle? Current { get; set; }
}
