// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Deployments;

// What the join account sees of its domain, read with its own credentials. The domain join check asks through this, so
// its verdicts are tested without a domain controller.
public interface IDomainDirectory
{
    // Throws DomainDirectoryException when the controller cannot be reached or refuses the sign-in.
    Task<DomainDirectoryFacts> ReadAsync(DomainDirectoryRequest request, CancellationToken cancellationToken);
}

// OrganizationalUnit: null for the domain's default Computers container.
public sealed record DomainDirectoryRequest(string Controller, string UserName, string Password, string? OrganizationalUnit)
{
    public override string ToString() =>
        $"DomainDirectoryRequest {{ Controller = {Controller}, UserName = {UserName}, OrganizationalUnit = {OrganizationalUnit} }}";
}

// Connection: how the server signed in, for the report. NamingContext: the domain the controller serves, as a
// distinguished name. Container: the organizational unit or the default Computers container, null when it does not
// exist. CanCreateComputers: the account may create computer objects there by a right of its own, without the quota.
// MachineAccountQuota: ms-DS-MachineAccountQuota of the domain, null when it could not be read. ComputersCreated: the
// computer accounts the join account created within that quota.
public sealed record DomainDirectoryFacts(
    string Connection,
    string NamingContext,
    string? Container,
    bool CanCreateComputers,
    int? MachineAccountQuota,
    int ComputersCreated);

public enum DomainDirectoryFailure
{
    // Nothing answered on LDAP, or no connection that keeps the password secret could be made.
    Unreachable,

    // The controller answered and refused the account. Detail holds Active Directory's reason code, such as 52e.
    SignInRefused,

    // Only LDAPS keeps the password secret on this operating system, and the controller offers none it trusts.
    NoSecureConnection,
}

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
