// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Messages;

namespace DDT.Core.Unattend;

public static class ComputerNames
{
    public const int MaxLength = 15;

    public static bool IsValid(string name, out string error)
    {
        ArgumentNullException.ThrowIfNull(name);

        error = Problem(name)?.Text ?? string.Empty;

        return error.Length == 0;
    }

    // Only the characters a DNS host name can contain, because on a domain the name becomes one. An invalid name fails
    // Windows setup in the specialize pass, after DDT has already reported the deployment as done.
    public static ServerMessage? Problem(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (string.IsNullOrWhiteSpace(name))
        {
            return ServerMessages.ComputerNameEmpty.With();
        }

        if (name.Length > MaxLength)
        {
            return ServerMessages.ComputerNameTooLong.With("max", MaxLength);
        }

        if (!name.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
        {
            return ServerMessages.ComputerNameCharacters.With();
        }

        if (name.All(char.IsAsciiDigit))
        {
            return ServerMessages.ComputerNameDigitsOnly.With();
        }

        if (name[0] == '-')
        {
            return ServerMessages.ComputerNameStartsWithHyphen.With();
        }

        return null;
    }
}
