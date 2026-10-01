// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Diagnostics;

namespace DDT.Host.Startup;

public sealed record CommandResult(int ExitCode, string Output)
{
    // Waits for the program, and keeps what it wrote
    public static CommandResult Run(string program, IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrEmpty(program);
        ArgumentNullException.ThrowIfNull(arguments);

        ProcessStartInfo start = new(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException($"{program} didn't start.");
        Task<string> error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        return new CommandResult(process.ExitCode, output + error.GetAwaiter().GetResult());
    }
}
