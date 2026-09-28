// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Authentication;

public enum ExternalSignInOutcome
{
    SignedIn,

    // No identity came back from the provider, or its account could not be saved.
    Failed,
    NotAllowed,
    NoRole,

    // Identity keeps the account in its two-factor cookie, and the sign-in page's code step finishes the sign-in.
    TwoFactor,
    LockedOut,

    // The identity belongs to no account, and single sign-on makes none.
    Unlinked,
    NotProvisioned,
}
