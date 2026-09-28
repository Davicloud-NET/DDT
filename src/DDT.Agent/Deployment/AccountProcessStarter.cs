// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using System.Runtime.Versioning;

namespace DDT.Agent.Deployment;

// Starts a tool as a signed-in account with CreateProcessAsUserW: .NET's Process goes through CreateProcessWithLogonW,
// which LocalSystem, the agent's account, may not call.
[SupportedOSPlatform("windows")]
public sealed class AccountProcessStarter(AgentLog log) : IAccountProcessStarter
{
    public IToolProcess Start(
        IAccountSession account,
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        IReadOnlyDictionary<string, string>? environment)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        ArgumentNullException.ThrowIfNull(arguments);

        if (account is not WindowsAccountSession windows)
        {
            throw new DeploymentStepException("This account cannot start a process on this computer.");
        }

        AccountToolProcess? started = null;

        try
        {
            started = new AccountToolProcess(log);
            started.Start(windows, WindowsCommandLine.Build(fileName, arguments), workingDirectory, environment);

            return started;
        }
        catch
        {
            started?.Dispose();

            throw;
        }
    }
}
