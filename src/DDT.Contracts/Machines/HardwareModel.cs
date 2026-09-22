// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Machines;

// A hardware model as the machine's firmware reports it, compared without regard to case or runs of spaces. A null
// manufacturer matches any, and a model that ends in * matches every model that starts with the text before it.
public sealed record HardwareModel(string? Manufacturer, string Model);
