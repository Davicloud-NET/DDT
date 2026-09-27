// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;

namespace DDT.Pxe;

// A listener could not bind its port. The message says which, and what to do about it, for an administrator.
public sealed class PxeBindException : InvalidOperationException
{
    public PxeBindException()
    {
    }

    public PxeBindException(string message)
        : base(message)
    {
    }

    public PxeBindException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public PxeBindException(ServerMessage reason, Exception innerException)
        : base(reason?.Text, innerException)
    {
        Reason = reason;
    }

    // The message as a code, for the settings page to say in the person's language.
    public ServerMessage? Reason { get; }
}
