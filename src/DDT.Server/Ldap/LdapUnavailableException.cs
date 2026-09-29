// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;

namespace DDT.Server.Ldap;

// The directory couldn't be queried. Either nothing answered, it refused the bind account, or it refused the search.
// The message tells an administrator which one.
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

    public LdapUnavailableException(ServerMessage reason, Exception innerException)
        : base(reason?.Text, innerException)
    {
        Reason = reason;
    }

    // The message as a code, so the web client can show it in the person's language.
    public ServerMessage? Reason { get; }
}
