// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// Ends a run that went to the installed Linux, so the machine starts over here. As with EraseQuestion, the console sends
// what was typed as Text, and anything but Word, or Back, restarts into the installed Linux.
public sealed record EndRunQuestion(bool Taken, string Word) : ConsoleQuestion;
