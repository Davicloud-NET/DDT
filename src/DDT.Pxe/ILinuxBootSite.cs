// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Pxe;

// The Linux boot environment as the server has it now. Netboot asks at every request, so the answer must be quick.
public interface ILinuxBootSite
{
    // Null while the environment cannot be started: its files are missing or damaged, or there is no root to hand out.
    LinuxBootBundle? Current { get; }
}
