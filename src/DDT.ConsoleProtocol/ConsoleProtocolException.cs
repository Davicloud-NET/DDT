// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// The other end sent something that is not a message of this protocol, or refused this end.
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
