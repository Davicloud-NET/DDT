// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.InstalledPhase;

// Keeps the machine from going to sleep while a run goes on. Disposing the hold lets it sleep again. A failure is a
// warning, never the run's end.
public interface IKeepAwake
{
    IDisposable Hold();
}
