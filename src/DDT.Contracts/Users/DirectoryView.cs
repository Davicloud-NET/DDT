// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

namespace DDT.Contracts.Users;

// The directory sign-in as configured, read-only here because the settings page edits it.
public sealed record DirectoryView(bool Enabled, string Host, string BaseDn, IReadOnlyList<DirectoryGroupMapping> GroupRoleMap);
