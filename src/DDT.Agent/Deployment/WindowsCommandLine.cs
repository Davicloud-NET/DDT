// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;

namespace DDT.Agent.Deployment;

// The command line CreateProcess takes, quoted the way Process quotes ProcessStartInfo.ArgumentList, so a tool gets the
// same arguments whether it runs as the agent or as an account. The file name is always quoted. An argument is only
// quoted when it's empty or holds a space, a tab or a quote, and backslashes before a quote are doubled.
public static class WindowsCommandLine
{
    public static string Build(string fileName, IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        ArgumentNullException.ThrowIfNull(arguments);

        StringBuilder line = new();
        line.Append('"').Append(fileName.Trim('"')).Append('"');

        foreach (string argument in arguments)
        {
            line.Append(' ');
            Append(line, argument);
        }

        return line.ToString();
    }

    private static void Append(StringBuilder line, string argument)
    {
        if (argument.Length != 0 && argument.AsSpan().IndexOfAny(' ', '\t', '"') < 0)
        {
            line.Append(argument);

            return;
        }

        line.Append('"');
        int index = 0;

        while (index < argument.Length)
        {
            char character = argument[index++];

            if (character == '\\')
            {
                int backslashes = 1;

                while (index < argument.Length && argument[index] == '\\')
                {
                    index++;
                    backslashes++;
                }

                if (index == argument.Length)
                {
                    line.Append('\\', backslashes * 2);
                }
                else if (argument[index] == '"')
                {
                    line.Append('\\', (backslashes * 2) + 1).Append('"');
                    index++;
                }
                else
                {
                    line.Append('\\', backslashes);
                }

                continue;
            }

            if (character == '"')
            {
                line.Append('\\').Append('"');

                continue;
            }

            line.Append(character);
        }

        line.Append('"');
    }
}
