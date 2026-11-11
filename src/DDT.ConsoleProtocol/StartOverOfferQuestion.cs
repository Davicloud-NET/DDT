// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// Shown for Seconds before the boot environment restarts into the installed Linux. Continue stops the restart, and
// EndRunQuestion follows. The agent withdraws it when the time is up. Taken says whether the installed Linux has the run.
public sealed record StartOverOfferQuestion(bool Taken, int Seconds) : ConsoleQuestion;
