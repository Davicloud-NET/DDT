// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Values;

// A value a rule or a machine role sets, such as ComputerName = PC-{{SerialNumber|alnum|right:12}}. Value is a
// template. Every signed-in user can read it, so it never holds a password; accounts hold those.
public sealed record NamedValue(string Name, string Value);
