// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Agent.WindowsPhase;

// <Windows>\DDT\session.json, which only SYSTEM can open: the console's pipe, which the session's shell names too, the
// answer file's sign-in password until the session is up, and the machine's settings as they were, to be put back.
public sealed record DeploySessionFile(string PipeName, string? Password, IReadOnlyList<SavedSetting>? Saved);
