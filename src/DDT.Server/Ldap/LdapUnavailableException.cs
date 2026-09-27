// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Ldap;

// The directory could not be asked: nothing answered, it refused the bind account, or it refused the search. The
// message says which, for an administrator.
public sealed class LdapUnavailableException : Exception
{
    public LdapUnavailableException()
    {
    }

    public LdapUnavailableException(string message)
        : base(message)
    {
    }

    public LdapUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
