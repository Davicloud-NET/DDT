// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Autopilot;

// Microsoft's refusal, in its own words. It never holds the request, so never the client secret.
public sealed class AutopilotDirectoryException : Exception
{
    public AutopilotDirectoryException()
    {
    }

    public AutopilotDirectoryException(string message)
        : base(message)
    {
    }

    public AutopilotDirectoryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
