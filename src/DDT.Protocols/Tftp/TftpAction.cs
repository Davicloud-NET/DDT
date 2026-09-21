// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Protocols.Tftp;

// The session emits actions rather than performing them, so it owns no socket and no timer and can
// be driven end to end by a test with a fake clock.
public abstract record TftpAction
{
    private protected TftpAction()
    {
    }
}
