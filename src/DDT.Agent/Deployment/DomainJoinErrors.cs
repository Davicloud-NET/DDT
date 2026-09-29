// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.ComponentModel;

namespace DDT.Agent.Deployment;

// Explains NetJoinDomain's error codes to whoever runs the sequence, and what to change. Some only mean that no domain
// controller answered yet. That's common while the network comes up after Windows started.
public static class DomainJoinErrors
{
    public const int FileNotFound = 2;
    public const int AccessDenied = 5;
    public const int InvalidParameter = 87;
    public const int BadNetworkPath = 53;
    public const int NetworkUnreachable = 1231;
    public const int NoLogonServers = 1311;
    public const int LogonFailure = 1326;
    public const int PasswordExpired = 1330;
    public const int AccountDisabled = 1331;
    public const int NoSuchDomain = 1355;
    public const int TimeSkew = 1398;
    public const int RpcServerUnavailable = 1722;
    public const int PasswordMustChange = 1907;
    public const int AccountLockedOut = 1909;

    // NERR_UserExists, NERR_SetupAlreadyJoined and NERR_AccountReuseBlockedByPolicy.
    public const int AccountExists = 2224;
    public const int AlreadyJoined = 2691;
    public const int AccountReuseBlocked = 2732;

    public const int MachineAccountQuotaExceeded = 8557;

    public static bool IsTransient(int code) =>
        code is BadNetworkPath or NetworkUnreachable or NoLogonServers or NoSuchDomain or RpcServerUnavailable;

    // organizationalUnit is the OU the join asked for, from the step or the server, or null for the default Computers
    // container. With an OU, the join answers "not found" when the domain has no such OU. It answers "parameter is
    // incorrect" when it can't use the name, as with a typo or the Computers container, which isn't an OU.
    public static string Describe(int code, string domain, string? organizationalUnit = null) => (code, organizationalUnit) switch
    {
        (FileNotFound, { Length: > 0 } ou) =>
            $"{domain} has no organizational unit {ou} (error {code}). Correct the organizational unit of the Join the domain step, or " +
            "DDT:Deployment:Domain:OrganizationalUnit on the server when the step names none.",
        (InvalidParameter, { Length: > 0 } ou) =>
            $"{domain} cannot put the computer account in {ou} (error {code}). Check its spelling: it must name an organizational unit of " +
            "that domain, such as OU=Workstations,DC=corp,DC=example. Leave it empty for the default Computers container, which cannot be named.",
        (AccessDenied, _) =>
            $"The join account may not add this computer to {domain} (error {code}). Give it the right to create computer objects in the " +
            "organizational unit, or create the computer account in advance.",
        (LogonFailure, _) =>
            $"{domain} did not accept the join account's user name or password (error {code}). Correct DDT:Deployment:Domain on the server.",
        (PasswordExpired, _) =>
            $"The join account's password has expired (error {code}). Give it a new one, in the domain and in DDT:Deployment:Domain on the server.",
        (AccountDisabled, _) => $"The join account is disabled in {domain} (error {code}).",
        (PasswordMustChange, _) => $"The join account has to change its password before it can join computers (error {code}).",
        (AccountLockedOut, _) => $"The join account is locked out of {domain} (error {code}).",
        (NoSuchDomain, _) =>
            $"The domain {domain} was not found (error {code}). Check that the DNS server this machine gets from DHCP knows the domain.",
        (BadNetworkPath or NetworkUnreachable or NoLogonServers or RpcServerUnavailable, _) =>
            $"No domain controller of {domain} could be reached (error {code}). Check that this machine's network reaches the domain controllers.",
        (TimeSkew, _) =>
            $"This machine's clock is too far from the domain controller's for Kerberos (error {code}). Correct the time in the firmware setup.",
        (AccountExists, _) =>
            $"{domain} already has a computer account with this machine's name, which the join account cannot take over (error {code}). " +
            "Delete the old computer account, or let the join account reset it.",
        (AlreadyJoined, _) =>
            $"Windows is already joined to a domain (error {code}). Capture the image from a Windows that is not joined, generalized with Sysprep.",
        (AccountReuseBlocked, _) =>
            $"{domain} already has a computer account with this machine's name, which another account created, and the domain's rules for " +
            $"reusing computer accounts (KB5020276) keep the join account from taking it over (error {code}). Delete the old computer account, " +
            "join with the account that created it, or allow the join account in the domain controllers' policy \"Domain controller: Allow " +
            "computer account re-use during domain join\".",
        (MachineAccountQuotaExceeded, _) =>
            $"The join account has added as many computers to {domain} as it may (error {code}, ms-DS-MachineAccountQuota). Give it the " +
            "right to create computer objects in the organizational unit, or create the computer account in advance.",
        _ => $"Joining {domain} failed with error {code}: {new Win32Exception(code).Message}",
    };
}
