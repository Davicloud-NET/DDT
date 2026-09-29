// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.ConsoleProtocol;

// A Pause step is waiting. Show Message and answer with Continue once the person is done. The agent withdraws the
// question when the run is continued on the web or the pause's time is up.
public sealed record PauseQuestion(string StepName, string Message) : ConsoleQuestion;
