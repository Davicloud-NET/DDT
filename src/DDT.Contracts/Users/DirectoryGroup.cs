// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Users;

// A group found in the directory, for choosing one by its name instead of typing its distinguished name.
public sealed record DirectoryGroup(string DistinguishedName, string? Name, string? Description);
