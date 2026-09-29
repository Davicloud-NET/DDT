// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static DDT.Agent.Deployment.AccountNativeMethods;

namespace DDT.Agent.Deployment;

// The environment block of a tool started as an account. It's the account's environment with DDT's variables on top,
// sorted and double-null-terminated the way CreateProcessAsUserW takes it.
[SupportedOSPlatform("windows")]
internal static unsafe class AccountEnvironment
{
    public static char* Build(SafeKernelHandle token, IReadOnlyDictionary<string, string>? added)
    {
        if (!CreateEnvironmentBlock(out char* profile, token, inherit: false))
        {
            throw DeploymentStepException.ForLastWin32Error("The script's environment could not be built");
        }

        try
        {
            SortedDictionary<string, string> variables = new(StringComparer.OrdinalIgnoreCase);
            Read(profile, variables);

            if (added is not null)
            {
                foreach ((string name, string value) in added)
                {
                    variables[name] = value;
                }
            }

            return Block(variables);
        }
        finally
        {
            _ = DestroyEnvironmentBlock(profile);
        }
    }

    public static void Free(char* block) => NativeMemory.Free(block);

    private static void Read(char* block, SortedDictionary<string, string> variables)
    {
        char* cursor = block;

        while (*cursor != '\0')
        {
            ReadOnlySpan<char> entry = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(cursor);
            cursor += entry.Length + 1;
            int equals = entry.IndexOf('=');

            // A name that starts with '=' is a drive's current directory, such as "=C:". That first '=' isn't the
            // separator.
            if (equals > 0)
            {
                variables[entry[..equals].ToString()] = entry[(equals + 1)..].ToString();
            }
        }
    }

    private static char* Block(SortedDictionary<string, string> variables)
    {
        int length = 1;

        foreach ((string name, string value) in variables)
        {
            length += name.Length + 1 + value.Length + 1;
        }

        char* block = (char*)NativeMemory.Alloc((nuint)length, sizeof(char));
        int index = 0;

        foreach ((string name, string value) in variables)
        {
            foreach (char character in name)
            {
                block[index++] = character;
            }

            block[index++] = '=';

            foreach (char character in value)
            {
                block[index++] = character;
            }

            block[index++] = '\0';
        }

        block[index] = '\0';

        return block;
    }

}
