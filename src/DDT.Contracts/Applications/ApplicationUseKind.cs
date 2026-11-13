// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Applications;

// Sent by name, so the values never change.
public enum ApplicationUseKind
{
    // An application that depends on it.
    Application,

    // A bundle that holds it.
    Bundle,

    // A sequence whose step or input names it.
    Sequence,
}
