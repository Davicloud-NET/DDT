// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Net;
using System.Text;

namespace DDT.Agent.Deployment;

// The options of a CIFS mount as the kernel reads them. They hold the password, so nothing logs them.
public static class CifsOptions
{
    // The kernel copies one page of options and no more.
    public const int MaxBytes = 4095;

    // The share is root's alone, and root starts programs from it. The address is passed because the kernel resolves
    // no name without a helper program. Null when the kernel's call cannot carry the account.
    public static string? For(IPAddress address, string userName, string password)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(userName);
        ArgumentNullException.ThrowIfNull(password);

        (string? domain, string user) = Account(userName);
        string options = string.Concat(
            $"ip={address},username={Escape(user)}",
            domain is null ? string.Empty : $",domain={Escape(domain)}",
            $",password={Escape(password)},sec=ntlmssp,file_mode=0700,dir_mode=0700");

        return options.Contains('\0', StringComparison.Ordinal) || Encoding.UTF8.GetByteCount(options) > MaxBytes ? null : options;
    }

    // The kernel splits the options at commas and takes two in a row as one that belongs to the value. After a single
    // one it would log the rest of a password as an unknown option.
    public static string Escape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return value.Replace(",", ",,", StringComparison.Ordinal);
    }

    // DOMAIN\user names the domain apart, and .\user the server's own accounts. user@domain goes to the server as it is.
    private static (string? Domain, string User) Account(string userName)
    {
        int slash = userName.IndexOf('\\', StringComparison.Ordinal);

        if (slash < 0)
        {
            return (null, userName);
        }

        return (userName[..slash] is "" or "." ? null : userName[..slash], userName[(slash + 1)..]);
    }
}
