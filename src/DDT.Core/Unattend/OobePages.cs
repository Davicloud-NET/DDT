// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Unattend;

// The out-of-box pages the answer file hides.
public sealed record OobePages(bool Eula, bool OemRegistration, bool OnlineAccounts, bool WirelessSetup)
{
    public static OobePages AllHidden { get; } = new(true, true, true, true);
}
