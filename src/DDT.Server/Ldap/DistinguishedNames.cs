// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Text;

namespace DDT.Server.Ldap;

public static class DistinguishedNames
{
    // The value of the first part, such as "DDT Operators" of "CN=DDT Operators,OU=Groups,DC=corp,DC=example", for
    // naming a group in a sentence without asking the directory. A name without "=" is returned whole.
    public static string FirstValue(string distinguishedName)
    {
        ArgumentNullException.ThrowIfNull(distinguishedName);

        StringBuilder value = new();
        bool escaped = false;
        bool inValue = !distinguishedName.Contains('=', StringComparison.Ordinal);

        foreach (char c in distinguishedName)
        {
            if (escaped)
            {
                escaped = false;

                if (inValue)
                {
                    value.Append(c);
                }
            }
            else if (c == '\\')
            {
                escaped = true;
            }
            else if (c == ',' && inValue)
            {
                break;
            }
            else if (c == '=' && !inValue)
            {
                inValue = true;
            }
            else if (inValue)
            {
                value.Append(c);
            }
        }

        return value.ToString().Trim();
    }
}
