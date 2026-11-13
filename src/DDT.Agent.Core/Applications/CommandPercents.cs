// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;

namespace DDT.Agent.Applications;

// An application's command as a batch file must hold it, so cmd runs it as typed: a batch file eats a lone %, as in
// for %i or %20. A %NAME% reference, with a :modifier or without, and a %% stay; every other % is doubled.
public static class CommandPercents
{
    public static string ForBatchFile(string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        StringBuilder text = new(command.Length);

        for (int index = 0; index < command.Length; index++)
        {
            if (command[index] != '%')
            {
                text.Append(command[index]);
            }
            else if (index + 1 < command.Length && command[index + 1] == '%')
            {
                text.Append("%%");
                index++;
            }
            else if (ReferenceEnd(command, index) is int end and >= 0)
            {
                text.Append(command, index, end - index + 1);
                index = end;
            }
            else
            {
                text.Append("%%");
            }
        }

        return text.ToString();
    }

    // Where the % that closes a reference opened at start is, or -1. A name starts with a letter or _, and a : starts
    // a modifier that runs to the next %.
    private static int ReferenceEnd(string command, int start)
    {
        int index = start + 1;

        if (index >= command.Length || !(char.IsAsciiLetter(command[index]) || command[index] == '_'))
        {
            return -1;
        }

        while (index < command.Length && IsNamePart(command[index]))
        {
            index++;
        }

        if (index < command.Length && command[index] == ':')
        {
            return command.IndexOf('%', index + 1);
        }

        return index < command.Length && command[index] == '%' ? index : -1;
    }

    private static bool IsNamePart(char character) => char.IsAsciiLetterOrDigit(character) || character is '_' or '(' or ')' or '.' or '-';
}
