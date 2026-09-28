// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Messages;

public static partial class ServerMessages
{
    // Organizational units, for a Join the domain step and the domain check.

    public static readonly MessageTemplate OrganizationalUnitWithPrefix = Define(
        "organizationalUnit.withPrefix",
        "Must be a distinguished name without the LDAP:// prefix, such as OU=Workstations,DC=example,DC=com.");

    public static readonly MessageTemplate OrganizationalUnitIsComputers = Define(
        "organizationalUnit.isComputers",
        "The default Computers container is no organizational unit and cannot be named. Leave this empty to use it.");

    public static readonly MessageTemplate OrganizationalUnitNotDistinguished = Define(
        "organizationalUnit.notDistinguished",
        "''{value}'' is not a distinguished name. Write it like OU=Workstations,DC=example,DC=com.");

    // The directory used for sign-ins, and the domain that Join the domain steps join.

    public static readonly MessageTemplate DirectoryEnterUserName = Define("directory.enterUserName", "Enter the user name to check.");

    public static readonly MessageTemplate DirectoryIncomplete = Define(
        "directory.connectionIncomplete",
        "The directory connection is not complete. Enter the directory server and the base DN on the Sign-in page.");

    public static readonly MessageTemplate DirectoryOff = Define(
        "directory.turnedOff",
        "Sign-in through a directory is off. Turn it on and set its connection on the Sign-in page first.");

    public static readonly MessageTemplate DirectoryBindRefused = Define(
        "directory.bindAccountRefused",
        "The directory at {server} refused the bind account {bindDn}. Check the bind account and its password on the Sign-in page.");

    public static readonly MessageTemplate DirectoryUnreachable = Define(
        "directory.unreachable",
        "The directory at {server} could not be reached: {detail}");

    public static readonly MessageTemplate DirectorySearchRefused = Define(
        "directory.baseDnRefused",
        "The directory at {server} refused the search under {baseDn}: {detail} Check the base DN on the Sign-in page.");

    public static readonly MessageTemplate DirectoryNoEntry = Define(
        "directory.noMatchingEntry",
        "No entry under {baseDn} matches {name} with the user filter, so a sign-in with it is refused.");

    public static readonly MessageTemplate DirectoryManyEntries = Define(
        "directory.manyMatchingEntries",
        "More than one entry under {baseDn} matches {name} with the user filter, so a sign-in with it is refused.");

    public static readonly MessageTemplate DirectoryLocalAccount = Define(
        "directory.localAccount",
        "{name} is a local account in DDT, so a sign-in with this name checks its DDT password and never asks the directory.");

    public static readonly MessageTemplate DirectorySingleSignOnAccount = Define(
        "directory.singleSignOnAccount",
        "{name} is a single sign-on account in DDT, so a sign-in with this name and a password is refused.");

    public static readonly MessageTemplate DirectoryNoImmutableId = Define(
        "directory.noImmutableId",
        "The entry has no {attribute} value to key the account on, so a sign-in is refused.");

    public static readonly MessageTemplate DirectoryAccountDisabled = Define(
        "directory.accountDisabled",
        "The DDT account {name} is disabled, so a sign-in is refused.");

    public static readonly MessageTemplate DirectoryNoMapNewAccount = Define(
        "directory.emptyMapNewAccount",
        "The directory's group map on the Sign-in page is empty, so administrators set roles. A first sign-in makes an account without " +
        "one, which reaches nothing until an administrator gives it a role.");

    public static readonly MessageTemplate DirectoryNoMapNoRole = Define(
        "directory.emptyMapNoRole",
        "The directory's group map on the Sign-in page is empty, so administrators set roles. The account has none yet, so it reaches " +
        "nothing.");

    public static readonly MessageTemplate DirectoryNoMapRole = Define(
        "directory.emptyMapRole",
        "The directory's group map on the Sign-in page is empty, so administrators set roles. The account has {role}.");

    public static readonly MessageTemplate DirectoryNoMappedGroup = Define(
        "directory.inNoMappedGroup",
        "{name} is in none of the groups the directory's group map on the Sign-in page gives a role, so a sign-in is refused.");

    public static readonly MessageTemplate DirectoryRoleFromGroup = Define("directory.roleFromGroup", "{name} gets {role} from {group}.");

    public static readonly MessageTemplate DirectoryRoleFromGroups = Define(
        "directory.roleFromGroups",
        "{name} is in {count} mapped groups and gets the highest role they give, {role} from {group}.");

    public static readonly MessageTemplate DirectoryLockedOut = Define(
        "directory.lockedOut",
        "{reason} The account is locked out for now, so a sign-in waits until the lockout ends.");

    public static readonly MessageTemplate DomainNotConfigured = Define(
        "domain.notSet",
        "No domain is set. Set the domain and the join account on the Deployment defaults page.");

    public static readonly MessageTemplate DomainNoJoinAccount = Define(
        "domain.noJoinAccountSet",
        "The join account is not set. Set it and its password on the Deployment defaults page.");

    public static readonly MessageTemplate DomainCheckError = Define(
        "domain.checkError",
        "{controller} answered the check with an error: {detail}");

    public static readonly MessageTemplate DomainSignedIn = Define(
        "domain.signedIn",
        "Signed in to {controller} as {user} over {connection}.");

    public static readonly MessageTemplate DomainOtherDomain = Define(
        "domain.servesOtherDomain",
        "{controller} serves the domain {namingContext}, not {domain} ({expected}). Correct the domain on the Deployment defaults " +
        "page, or name a domain controller of that domain for the check there.");

    public static readonly MessageTemplate DomainControllerOf = Define(
        "domain.controllerOf",
        "{controller} is a domain controller of {domain}.");

    public static readonly MessageTemplate DomainNoComputersContainer = Define(
        "domain.noComputersContainer",
        "The default Computers container of {domain} was not found, or {user} may not read it.");

    public static readonly MessageTemplate DomainNoOrganizationalUnit = Define(
        "domain.organizationalUnitMissing",
        "{domain} has no organizational unit {organizationalUnit}, or {user} may not read it. Correct the organizational unit of the " +
        "Join the domain step, or the one on the Deployment defaults page when the step names none.");

    public static readonly MessageTemplate DomainMayCreate = Define(
        "domain.mayCreate",
        "{user} may create computer objects in {container}, as many as it needs.");

    public static readonly MessageTemplate DomainMayNotCreateInUnit = Define(
        "domain.mayNotCreateInUnit",
        "{user} may not create computer objects in {container}, and the machine account quota does not reach an organizational unit. " +
        "Delegate \"Create Computer objects\" on it to the account, for example with the Delegation of Control wizard of Active " +
        "Directory Users and Computers.");

    public static readonly MessageTemplate DomainQuotaUnknown = Define(
        "domain.quotaUnknown",
        "{user} may not create computer objects in {container} by a right of its own, and the domain's machine account quota could " +
        "not be read. Delegate \"Create Computer objects\" on the container to the account.");

    public static readonly MessageTemplate DomainQuotaUsed = Define(
        "domain.quotaUsed",
        "{user} may not create computer objects in {container} by a right of its own, and has used its machine account quota: " +
        "{created} of {quota} (ms-DS-MachineAccountQuota). Delegate \"Create Computer objects\" on the container to the account.");

    public static readonly MessageTemplate DomainQuotaLeft = Define(
        "domain.quotaLeft",
        "{user} may not create computer objects in {container} by a right of its own, so it joins within the domain's machine " +
        "account quota: {created} of {quota} used, {left} left. That also needs the user right \"Add workstations to domain\", which " +
        "Authenticated Users hold by default and this check cannot read. Delegate \"Create Computer objects\" on the container to the " +
        "account to join more.");

    public static readonly MessageTemplate DomainNoSuchAccount = Define(
        "domain.joinAccountUnknown",
        "{controller} knows no account {user}. Correct the join account on the Deployment defaults page.");

    public static readonly MessageTemplate DomainWrongPassword = Define(
        "domain.joinPasswordRefused",
        "{controller} did not accept the password of {user}. Correct the join account password on the Deployment defaults page.");

    public static readonly MessageTemplate DomainLogonHours = Define(
        "domain.logonHours",
        "{user} may not sign in at this time of day (logon hours).");

    public static readonly MessageTemplate DomainLogonWorkstations = Define(
        "domain.logonWorkstations",
        "{user} may not sign in from the DDT server (Log On To workstations).");

    public static readonly MessageTemplate DomainPasswordExpired = Define(
        "domain.joinPasswordExpired",
        "The password of {user} has expired. Give it a new one, in the domain and on the Deployment defaults page.");

    public static readonly MessageTemplate DomainAccountDisabled = Define("domain.accountDisabled", "{user} is disabled.");

    public static readonly MessageTemplate DomainAccountExpired = Define("domain.accountExpired", "{user} has expired.");

    public static readonly MessageTemplate DomainMustChangePassword = Define(
        "domain.joinPasswordMustChange",
        "{user} has to change its password before it can sign in. Give it a new one, in the domain and on the Deployment defaults page.");

    public static readonly MessageTemplate DomainLockedOut = Define("domain.lockedOut", "{user} is locked out.");

    public static readonly MessageTemplate DomainSignInRefused = Define(
        "domain.signInRefused",
        "{controller} did not accept the user name or password of {user}.");

    public static readonly MessageTemplate DomainSignInRefusedWithReason = Define(
        "domain.signInRefusedWithReason",
        "{controller} did not accept the user name or password of {user} (reason {reason}).");

    public static readonly MessageTemplate DomainUnreachable = Define(
        "domain.controllerUnreachable",
        "{controller} could not be reached over LDAP. If this server's DNS does not know {domain}, enter a domain controller's name or " +
        "address for the check on the Deployment defaults page. The machines find their domain controller through their own DNS.");

    public static readonly MessageTemplate DomainUnreachableWithDetail = Define(
        "domain.controllerUnreachableWithDetail",
        "{controller} could not be reached over LDAP ({detail}). If this server's DNS does not know {domain}, enter a domain " +
        "controller's name or address for the check on the Deployment defaults page. The machines find their domain controller " +
        "through their own DNS.");

    public static readonly MessageTemplate DomainNoSecureConnection = Define(
        "domain.noSecureConnection",
        "{controller} offers no LDAPS on port 636 that this server trusts, and on this operating system only LDAPS keeps the join " +
        "account's password secret during the check. Give the domain controllers a certificate, for example from Active Directory " +
        "Certificate Services, and trust its CA on this server. Joining does not depend on this check.");
}
