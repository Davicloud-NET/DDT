// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Pxe;

// Which boot environment each machine the server knows should start. Asked for every PXE request, so it answers from
// memory and never waits.
public interface IBootChoice
{
    BootWish For(NetbootMachine machine);
}
