// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.MachineConsole.ViewModels;

// A fact about the machine, like its MAC address. Mono sets it in the monospace font used for identifiers.
public sealed record Fact(string Label, string Value, bool Mono = false);
