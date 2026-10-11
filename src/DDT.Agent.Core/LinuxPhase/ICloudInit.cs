// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.LinuxPhase;

// What the installed Linux says of cloud-init at this start. Each text is a file's content, null when there is none.
public interface ICloudInit
{
    // Whether cloud-init runs at this start. Its generator says so before any unit.
    bool Enabled { get; }

    // result.json, which is there once cloud-init has finished at this start.
    string? Result();

    // status.json, with what each stage recovered from.
    string? Status();

    // The merged cloud-config, which only root reads.
    string? Configuration();

    // What systemd keeps of a shutdown someone scheduled.
    string? ScheduledShutdown();

    // The kernel's id of this start of Linux, which every start makes anew. Null where it cannot be read.
    string? StartId();
}
