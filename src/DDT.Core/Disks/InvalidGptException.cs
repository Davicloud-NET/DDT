// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;

namespace DDT.Core.Disks;

// The message is a sentence for the operator saying what is wrong with the partition table.
public sealed class InvalidGptException : Exception
{
    public InvalidGptException()
    {
    }

    public InvalidGptException(string message)
        : base(message)
    {
    }

    public InvalidGptException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public InvalidGptException(ServerMessage reason)
        : base(reason?.Text)
    {
        Reason = reason;
    }

    public InvalidGptException(ServerMessage reason, Exception innerException)
        : base(reason?.Text, innerException)
    {
        Reason = reason;
    }

    // The message as a code, for the web to say in the person's language.
    public ServerMessage? Reason { get; }
}
