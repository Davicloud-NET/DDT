// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.WindowsPhase;

// SERVICE_STATUS as SetServiceStatus reads it: seven DWORDs in this order, which the record's fields keep.
public readonly record struct ServiceStatus(
    uint ServiceType,
    uint CurrentState,
    uint ControlsAccepted,
    uint Win32ExitCode,
    uint ServiceSpecificExitCode,
    uint CheckPoint,
    uint WaitHint);
