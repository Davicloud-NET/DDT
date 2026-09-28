// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.IO.Pipes;
using System.Security.Cryptography;

namespace DDT.ConsoleProtocol;

// The named pipe between the agent and its console, passed as `--pipe <name>` with a random name. Only one end, of the
// agent's own account, can connect; in the installed Windows the console runs with `--session` as DDT's session shell.
public static class ConsolePipe
{
    // The console's executable, which the agent looks for next to itself.
    public const string FileName = "ddt-console.exe";

    public const string PipeArgument = "--pipe";

    public const string SessionArgument = "--session";

    private const string NamePrefix = "ddt-console-";

    // What the console is made of: the executable and the two libraries it draws with, which go wherever it goes.
    public static IReadOnlyList<string> Files { get; } = [FileName, "libSkiaSharp.dll", "libHarfBuzzSharp.dll"];

    public static bool IsSession(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        return args.Contains(SessionArgument, StringComparer.Ordinal);
    }

    public static string NewName() => NamePrefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    // Fails when a pipe of that name exists already, so nothing that got there first can pose as the agent's pipe.
    public static NamedPipeServerStream CreateServer(string name) =>
        new(
            name,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance);

    // Connects only to a pipe of this process's own account.
    public static NamedPipeClientStream CreateClient(string name) =>
        new(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    // The pipe name in a console's command line, or null when there is none.
    public static string? NameFrom(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        for (int index = 0; index < args.Count - 1; index++)
        {
            if (args[index] == PipeArgument && args[index + 1].StartsWith(NamePrefix, StringComparison.Ordinal))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}
