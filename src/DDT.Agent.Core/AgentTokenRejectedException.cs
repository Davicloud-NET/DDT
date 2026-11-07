// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent;

// The server answered 401: the token expired, or the machine was re-registered, rejected or retired.
public sealed class AgentTokenRejectedException : Exception
{
    public AgentTokenRejectedException()
        : base("The server no longer accepts this machine's token.")
    {
    }

    public AgentTokenRejectedException(string message)
        : base(message)
    {
    }

    public AgentTokenRejectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
