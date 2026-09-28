// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Deployments;

public enum DomainDirectoryFailure
{
    // Nothing answered on LDAP, or no connection that keeps the password secret could be made.
    Unreachable,

    // The controller answered and refused the account. Detail holds Active Directory's reason code, such as 52e.
    SignInRefused,

    // Only LDAPS keeps the password secret on this operating system, and the controller offers none it trusts.
    NoSecureConnection,
}
