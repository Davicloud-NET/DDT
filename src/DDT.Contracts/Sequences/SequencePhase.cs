// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Sequences;

// Stored and sent by name. New phases go at the end.
public enum SequencePhase
{
    WindowsPE,
    Windows,
    // The netbooted Linux that writes a Linux sequence's disk. Version 4.
    LinuxBoot,
    // The installed Linux, where a Linux sequence goes on. Version 5.
    Linux,
}
