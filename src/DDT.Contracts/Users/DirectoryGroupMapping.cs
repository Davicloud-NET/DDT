// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Users;

// Group is the distinguished name the map names. Name is the group's common name in the directory. It's null when
// the directory has no such group or couldn't be reached.
public sealed record DirectoryGroupMapping(string Group, string? Name, string Role);
