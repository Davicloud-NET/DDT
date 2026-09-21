// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Protocols.Tftp;

public readonly record struct TftpStep(TftpSessionState State, IReadOnlyList<TftpAction> Actions)
{
    public static TftpStep Nothing(TftpSessionState state) => new(state, []);
}
