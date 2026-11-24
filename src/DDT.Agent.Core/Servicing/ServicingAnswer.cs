// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.Servicing;

// What one DISM call did, and the image's version it printed before it began. Null when it printed none.
public sealed record ServicingAnswer(ServicingOutcome Outcome, Version? ImageVersion);
