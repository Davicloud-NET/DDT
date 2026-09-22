// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.ComponentModel;

namespace DDT.Agent.Deployment;

// What NetJoinDomain's answers mean for whoever runs the sequence, and what to change. Some only say that no domain
// controller answered yet, which is common while the network comes up after Windows started.
public static class DomainJoinErrors
{
    public const int AccessDenied = 5;
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

    public static string Describe(int code, string domain) => code switch
    {
        AccessDenied =>
            $"The join account may not add this computer to {domain} (error {code}). Give it the right to create computer objects in the " +
            "organizational unit, or create the computer account in advance.",
        LogonFailure =>
            $"{domain} did not accept the join account's user name or password (error {code}). Correct DDT:Deployment:Domain on the server.",
        PasswordExpired =>
            $"The join account's password has expired (error {code}). Give it a new one, in the domain and in DDT:Deployment:Domain on the server.",
        AccountDisabled => $"The join account is disabled in {domain} (error {code}).",
        PasswordMustChange => $"The join account has to change its password before it can join computers (error {code}).",
        AccountLockedOut => $"The join account is locked out of {domain} (error {code}).",
        NoSuchDomain =>
            $"The domain {domain} was not found (error {code}). Check that the DNS server this machine gets from DHCP knows the domain.",
        BadNetworkPath or NetworkUnreachable or NoLogonServers or RpcServerUnavailable =>
            $"No domain controller of {domain} could be reached (error {code}). Check that this machine's network reaches the domain controllers.",
        TimeSkew =>
            $"This machine's clock is too far from the domain controller's for Kerberos (error {code}). Correct the time in the firmware setup.",
        AccountExists =>
            $"{domain} already has a computer account with this machine's name, which the join account cannot take over (error {code}). " +
            "Delete the old computer account, or let the join account reset it.",
        AlreadyJoined =>
            $"Windows is already joined to a domain (error {code}). Capture the image from a Windows that is not joined, generalized with Sysprep.",
        AccountReuseBlocked =>
            $"{domain} already has a computer account with this machine's name, which another account created, and the domain's rules for " +
            $"reusing computer accounts (KB5020276) keep the join account from taking it over (error {code}). Delete the old computer account, " +
            "join with the account that created it, or allow the join account in the domain controllers' policy \"Domain controller: Allow " +
            "computer account re-use during domain join\".",
        MachineAccountQuotaExceeded =>
            $"The join account has added as many computers to {domain} as it may (error {code}, ms-DS-MachineAccountQuota). Give it the " +
            "right to create computer objects in the organizational unit, or create the computer account in advance.",
        _ => $"Joining {domain} failed with error {code}: {new Win32Exception(code).Message}",
    };
}
