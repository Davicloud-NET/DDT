// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.LinuxPhase;

// The cloud-init seeds of the installed Linux, each as a folder whose files the agent can change.
public interface IMountedSeeds
{
    // Mounts each seed in turn, hands its folder to visit and unmounts it. Returns how many it mounted. Never throws
    // for a seed that cannot be mounted.
    int Each(Action<string> visit);
}
