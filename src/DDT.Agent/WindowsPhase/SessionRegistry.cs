// Copyright (C) 2026 Davicloud
// SPDX-License-Identifier: GPL-3.0-or-later
// Part of DDT, the Davicloud Deployment Toolkit. Additional terms under GPL section 7 apply, see NOTICE.

using DDT.Agent.Deployment;
using Microsoft.Win32;

namespace DDT.Agent.WindowsPhase;

// Where DDT's session writes its settings: HKEY_LOCAL_MACHINE and HKEY_USERS, unless a test says otherwise, and the
// tools that run reg.exe to load the account's hive while it is not signed in.
public sealed record SessionRegistry(RegistryKey Machine, RegistryKey Users, IToolRunner Tools);
