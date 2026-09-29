// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Deployments;

public sealed class DomainDirectoryException : Exception
{
    public DomainDirectoryException()
    {
    }

    public DomainDirectoryException(string message)
        : base(message)
    {
    }

    public DomainDirectoryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public DomainDirectoryException(DomainDirectoryFailure failure, string? detail, Exception? innerException = null)
        : base($"{failure}: {detail}", innerException)
    {
        Failure = failure;
        Detail = detail;
    }

    public DomainDirectoryFailure Failure { get; }

    public string? Detail { get; }
}
