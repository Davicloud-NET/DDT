// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

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
}
