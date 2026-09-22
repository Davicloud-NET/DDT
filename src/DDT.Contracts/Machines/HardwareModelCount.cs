// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Machines;

// A model the registered machines report, with how many report it, for the pickers of rules and package targets.
public sealed record HardwareModelCount(string? Manufacturer, string Model, int Machines);
