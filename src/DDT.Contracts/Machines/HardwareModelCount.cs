// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Machines;

// A model that registered machines report, and how many report it. The pickers for rules and package targets use it.
public sealed record HardwareModelCount(string? Manufacturer, string Model, int Machines);
