// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// Thrown when the other end sends something that isn't a message of this protocol, or refuses this end.
public sealed class ConsoleProtocolException : Exception
{
    public ConsoleProtocolException()
    {
    }

    public ConsoleProtocolException(string message)
        : base(message)
    {
    }

    public ConsoleProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
