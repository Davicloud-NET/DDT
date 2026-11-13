// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Applications;

// One application's entry in the checkpoint. Attempts counts the starts of a Started one and is 0 otherwise.
public sealed record ApplicationMark(ApplicationState State, int Attempts = 0);
