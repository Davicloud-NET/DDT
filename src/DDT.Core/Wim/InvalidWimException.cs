// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Core.Wim;

// The message is a sentence for the operator saying why the file was refused.
public sealed class InvalidWimException : Exception
{
    public InvalidWimException()
    {
    }

    public InvalidWimException(string message)
        : base(message)
    {
    }

    public InvalidWimException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
