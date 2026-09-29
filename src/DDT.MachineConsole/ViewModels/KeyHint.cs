// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.MachineConsole.ViewModels;

// A key in the footer: what it is, what it does, and whether what it opens is open.
public sealed record KeyHint(string Key, string Label, bool IsActive, Command Command);
