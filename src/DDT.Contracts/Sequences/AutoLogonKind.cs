// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// Who Windows signs in as by itself after the deployment.
public enum AutoLogonKind
{
    None,

    // The local administrator of the Deployment defaults, which the step adds.
    LocalAdministrator,

    // A library account or an Account input.
    Account,
}
