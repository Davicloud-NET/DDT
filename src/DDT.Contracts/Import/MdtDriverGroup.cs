// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Import;

// A folder of Out-of-Box Drivers with drivers of its own. Manufacturer and Model are set when its path ends in a maker
// and a model, the common layout.
public sealed record MdtDriverGroup(string Id, string Name, int Drivers, long SizeBytes, string? Manufacturer, string? Model);
