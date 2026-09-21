// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Server.Authentication;

public static class DdtPolicies
{
    public const string Administrator = "ddt.administrator";
    public const string Operator = "ddt.operator";
    public const string Viewer = "ddt.viewer";
    public const string Machine = "ddt.machine";
    public const string MachineAgent = "ddt.machine.agent";
}
