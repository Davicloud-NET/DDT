// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.ConsoleProtocol;

namespace DDT.Agent;

// A request to the server that ran out of time, with how far it got, which the console at the machine shows. The message
// says which limit ran out.
public sealed class ServerTimeoutException : TimeoutException
{
    public ServerTimeoutException()
    {
    }

    public ServerTimeoutException(string message)
        : base(message)
    {
    }

    public ServerTimeoutException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ServerTimeoutException(ConnectionStage stage, string message, Exception innerException)
        : base(message, innerException) => Stage = stage;

    public ConnectionStage Stage { get; } = ConnectionStage.Answer;
}
