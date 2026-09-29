// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Images;

// A conversion tool failed. The message names the tool and what it said.
public sealed class ConversionFailedException : Exception
{
    public ConversionFailedException()
    {
    }

    public ConversionFailedException(string message)
        : base(message)
    {
    }

    public ConversionFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
