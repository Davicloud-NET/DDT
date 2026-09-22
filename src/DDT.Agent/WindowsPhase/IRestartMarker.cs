// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.WindowsPhase;

// Whether a restart of Windows that the run asked for is still due. Only that restart clears it.
public interface IRestartMarker
{
    bool IsSet { get; }

    void Set();
}
