// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Machines;

// Each purpose gets its own protector, so a poll token issued to a Pending machine cannot be
// presented as a session token, and neither can be replayed as a resume token.
public enum MachineTokenPurpose
{
    Poll,
    Session,
    Resume,
}
