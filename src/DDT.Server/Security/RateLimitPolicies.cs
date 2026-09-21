// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Security;

public static class RateLimitPolicies
{
    public const string SignIn = "ddt.signin";
    public const string AgentRegistration = "ddt.agent.registration";
    public const string AgentMachine = "ddt.agent.machine";
    public const string AgentRelease = "ddt.agent.release";
    public const string AgentDownload = "ddt.agent.download";
    public const string AgentImage = "ddt.agent.image";
}
