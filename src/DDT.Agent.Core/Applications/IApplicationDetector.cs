// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Contracts.Applications;

namespace DDT.Agent.Applications;

public interface IApplicationDetector
{
    // Whether the machine has what the rule looks for.
    bool IsInstalled(ApplicationDetection detection);
}
