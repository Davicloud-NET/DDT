// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Deployment;

public interface IAccountProcessStarter
{
    // Starts fileName as the account, which is already signed in, in options' working directory. The process gets the
    // account's environment plus options' variables. A tool that can't start throws DeploymentStepException.
    IToolProcess Start(IAccountSession account, string fileName, IReadOnlyList<string> arguments, ToolRunOptions options);
}
